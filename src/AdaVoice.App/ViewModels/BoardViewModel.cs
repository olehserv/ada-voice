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

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingTake))]
    private RecordingResult? _pendingTake;

    [ObservableProperty]
    private string _newTitle = "";

    [ObservableProperty]
    private string? _notice;

    public BoardViewModel(IPlaybackHost playback, IRecorderHost recorder, StatusViewModel status)
    {
        _playback = playback;
        _recorder = recorder;
        Status = status;
        Phrases = new ObservableCollection<PhraseEntry>(_playback.Phrases);
    }

    public StatusViewModel Status { get; }

    /// <summary>The catalogued phrases shown as buttons. An ObservableCollection so the UI updates when
    /// a phrase is added (a plain List + OnPropertyChanged does not refresh an ItemsControl). The
    /// library list stays the persistence source of truth; this mirrors it for the UI.</summary>
    public ObservableCollection<PhraseEntry> Phrases { get; }

    /// <summary>True when a just-recorded take is waiting to be named/saved.</summary>
    public bool HasPendingTake => PendingTake is not null;

    // ---- Playback -------------------------------------------------------------------------------

    [RelayCommand]
    private void Play(PhraseEntry? entry)
    {
        if (entry is not null)
            _playback.PlayEntry(entry);
    }

    [RelayCommand]
    private void Stop() => _playback.StopPhrase();

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
        Phrases.Add(entry); // CollectionChanged -> the new phrase button appears immediately
    }

    [RelayCommand]
    private void DiscardTake()
    {
        PendingTake = null;
        Notice = "Take discarded.";
    }
}
