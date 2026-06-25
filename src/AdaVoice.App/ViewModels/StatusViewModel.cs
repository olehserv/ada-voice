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
    private EngineState _state;

    public StatusViewModel(IPlaybackHost host, Action<Action>? onUiThread = null)
    {
        _host = host;
        _onUiThread = onUiThread ?? (action => action()); // default: inline (unit tests)
        _state = host.State;
        host.StateChanged += OnStateChanged;
    }

    public string StateLabel => State switch
    {
        EngineState.Live => "LIVE",
        EngineState.OffAir => "OFF AIR",
        EngineState.Degraded => "DEGRADED",
        _ => "STOPPED",
    };

    /// <summary>True while recording: the call feed is paused. The view shows an OFF AIR banner.</summary>
    public bool IsOffAir => State == EngineState.OffAir;

    /// <summary>True only when Live — phrases can play, so the board's phrase buttons are enabled.</summary>
    public bool IsLive => State == EngineState.Live;

    private void OnStateChanged(object? sender, EngineState state) => _onUiThread(() => State = state);

    public void Dispose() => _host.StateChanged -= OnStateChanged;
}
