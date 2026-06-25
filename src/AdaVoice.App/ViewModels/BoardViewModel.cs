using AdaVoice.Audio.Engine;
using AdaVoice.Core.Domain;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// The Board: shows the phrases and triggers playback to the call. Talks only to <see cref="IPlaybackHost"/>,
/// so it is unit-testable with a fake host.
/// </summary>
public partial class BoardViewModel : ObservableObject
{
    private readonly IPlaybackHost _host;

    public BoardViewModel(IPlaybackHost host, StatusViewModel status)
    {
        _host = host;
        Status = status;
    }

    public StatusViewModel Status { get; }

    /// <summary>The catalogued phrases shown as buttons (loaded once at startup).</summary>
    public IReadOnlyList<PhraseEntry> Phrases => _host.Phrases;

    /// <summary>Play a phrase to the call (the cable). Needs the engine Live.</summary>
    [RelayCommand]
    private void Play(PhraseEntry? entry)
    {
        if (entry is not null)
            _host.PlayEntry(entry);
    }

    /// <summary>Stop the phrase currently playing (the big STOP).</summary>
    [RelayCommand]
    private void Stop() => _host.StopPhrase();

    /// <summary>Go Live: mic to the cable, phrases can play.</summary>
    [RelayCommand]
    private void StartEngine() => _host.Start();

    /// <summary>Stop the engine entirely.</summary>
    [RelayCommand]
    private void StopEngine() => _host.Stop();

    /// <summary>Toggle OFF AIR (the call feed is paused while OFF AIR).</summary>
    [RelayCommand]
    private void ToggleOffAir()
    {
        if (_host.State == EngineState.OffAir)
            _host.ExitOffAir();
        else
            _host.EnterOffAir();
    }
}
