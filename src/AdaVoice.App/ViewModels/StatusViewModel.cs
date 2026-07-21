using AdaVoice.App.Resources;
using AdaVoice.Audio.Engine;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Shows the engine state in the status bar. Subscribes to the host's <see cref="IPlaybackHost.StateChanged"/>
/// (which fires on the engine control thread) and marshals updates through an injected callback — the App
/// passes the WPF Dispatcher; tests run it inline. This keeps the view-model free of any WPF dependency.
/// </summary>
public partial class StatusViewModel : ObservableObject, IDisposable
{
    private readonly IPlaybackHost _host;
    private readonly Action<Action> _onUiThread;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(IsOffAir))]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(IsEngineRunning))]
    private EngineState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateErrorText))]
    private EngineError? _stateError;

    /// <summary>The localized reason the engine landed in the current state (e.g. a failed Start's
    /// reason), or null. Without this, "VB-Cable missing" looks like "the Start button does nothing".
    /// Audio carries only the raw <see cref="EngineError"/>, never display text.</summary>
    public string? StateErrorText => StateError is { } error ? Describe(error) : null;

    private static string Describe(EngineError error) => error.Reason switch
    {
        // Framework/system text (a caught exception's message) — stays English by design, not our
        // own text (same boundary as LibraryArchiveService.ImportResult.ExceptionMessage).
        EngineErrorReason.DeviceFailure => error.Detail ?? "",
        EngineErrorReason.DeviceChanged => Strings.Status_DeviceChanged,
        EngineErrorReason.CableStalled => Strings.Status_CableStalled,
        EngineErrorReason.TooManyMicChannels => string.Format(Strings.Status_TooManyMicChannelsFormat, error.Channels),
        EngineErrorReason.CableSampleRateMismatch => Strings.Status_CableSampleRateMismatch,
        _ => "",
    };

    public StatusViewModel(IPlaybackHost host, Action<Action>? onUiThread = null)
    {
        _host = host;
        _onUiThread = onUiThread ?? (action => action()); // default: inline (unit tests)
        _state = host.State;
        host.StateChanged += OnStateChanged;
    }

    public string StateLabel => State switch
    {
        EngineState.Live => Strings.Status_Live,
        EngineState.OffAir => Strings.Status_OffAir,
        EngineState.Degraded => Strings.Status_Degraded,
        _ => Strings.Status_Stopped,
    };

    /// <summary>True while recording: the call feed is paused. The view shows an OFF AIR banner.</summary>
    public bool IsOffAir => State == EngineState.OffAir;

    /// <summary>True only when Live — phrases actually reach the call in this state.</summary>
    public bool IsLive => State == EngineState.Live;

    /// <summary>The engine can be started only from the stopped state — drives the Start button.</summary>
    public bool CanStart => State == EngineState.Stopped;

    /// <summary>True in any non-stopped state — drives Stop engine, OFF AIR, and the panic STOP. These
    /// stay usable off air and even when degraded; the panic STOP is a no-op when nothing is playing,
    /// so keeping it live costs nothing and matches operators' expectations.</summary>
    public bool IsEngineRunning => State != EngineState.Stopped;

    private void OnStateChanged(object? sender, EngineStateChangedEventArgs e) => _onUiThread(() =>
    {
        State = e.State;
        StateError = e.Error;
    });

    public void Dispose() => _host.StateChanged -= OnStateChanged;
}
