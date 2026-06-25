using System.Collections.ObjectModel;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Core.Domain;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// The Board: shows the phrases, plays them to the call, and records new ones. Talks only to the host
/// seams (<see cref="IPlaybackHost"/> / <see cref="IRecorderHost"/>), so it is unit-testable with a fake.
/// </summary>
public partial class BoardViewModel : ObservableObject
{
    private readonly IPlaybackHost _playback;
    private readonly IRecorderHost _recorder;
    private readonly Action<Action> _onUiThread;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingTake))]
    private RecordingResult? _pendingTake;

    [ObservableProperty]
    private string _newTitle = "";

    [ObservableProperty]
    private string? _notice;

    public BoardViewModel(IPlaybackHost playback, IRecorderHost recorder, StatusViewModel status,
        Action<Action>? onUiThread = null)
    {
        _playback = playback;
        _recorder = recorder;
        _onUiThread = onUiThread ?? (action => action()); // default: inline (unit tests)
        Status = status;
        Phrases = new ObservableCollection<PhraseItemViewModel>(
            _playback.Phrases.Select(e => new PhraseItemViewModel(e)));
        _playback.PlayingPhraseChanged += OnPlayingPhraseChanged;
    }

    public StatusViewModel Status { get; }

    /// <summary>The phrase buttons. An ObservableCollection so the UI updates when one is added; each
    /// item carries its own UI state (e.g. the playing glow). The library list stays the source of
    /// truth; this mirrors it for the UI.</summary>
    public ObservableCollection<PhraseItemViewModel> Phrases { get; }

    /// <summary>True when a just-recorded take is waiting to be named/saved.</summary>
    public bool HasPendingTake => PendingTake is not null;

    // ---- Playback -------------------------------------------------------------------------------

    [RelayCommand]
    private void Play(PhraseItemViewModel? item)
    {
        if (item is not null)
            _playback.PlayEntry(item.Entry);
    }

    [RelayCommand]
    private void Stop() => _playback.StopPhrase();

    /// <summary>Reflect the engine's currently-playing phrase as the per-item glow. Fires off the UI
    /// thread, so marshal before touching the bound items.</summary>
    private void OnPlayingPhraseChanged(object? sender, string? playingId) =>
        _onUiThread(() =>
        {
            foreach (var item in Phrases)
                item.IsPlaying = playingId is not null && item.Entry.Id == playingId;
        });

    [RelayCommand]
    private void StartEngine() => _playback.Start();

    [RelayCommand]
    private void StopEngine() => _playback.Stop();

    [RelayCommand]
    private void ToggleOffAir()
    {
        if (_playback.State == EngineState.OffAir)
            _playback.ExitOffAir();
        else
            _playback.EnterOffAir();
    }

    // ---- Recording ------------------------------------------------------------------------------

    [RelayCommand]
    private void StartRecording()
    {
        Notice = null;
        if (_recorder.TryStartRecording())
            IsRecording = true;
        else
            Notice = "Press Start to go Live before recording.";
    }

    [RelayCommand]
    private void StopRecording()
    {
        IsRecording = false;
        var take = _recorder.StopRecording();
        if (take is { HasSignal: true })
        {
            PendingTake = take;
            NewTitle = $"Take {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            Notice = null;
        }
        else
        {
            PendingTake = null;
            Notice = "No signal — nothing recorded.";
        }
    }

    [RelayCommand]
    private void PreviewTake()
    {
        if (PendingTake is { } take)
            Notice = _recorder.Preview(take.Samples, take.GainDb) ?? "Previewing…";
    }

    [RelayCommand]
    private void SaveTake()
    {
        if (PendingTake is not { } take)
            return;

        var entry = _recorder.SaveTake(take, NewTitle);
        PendingTake = null;
        Notice = $"Saved \"{entry.Title}\".";
        Phrases.Add(new PhraseItemViewModel(entry)); // appears on the board immediately
    }

    [RelayCommand]
    private void DiscardTake()
    {
        PendingTake = null;
        Notice = "Take discarded.";
    }
}
