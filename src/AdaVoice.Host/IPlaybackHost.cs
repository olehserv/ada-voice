using AdaVoice.Audio.Engine;
using AdaVoice.Core.Domain;

namespace AdaVoice.Host;

/// <summary>Why a play/preview attempt failed — the App layer's localization key, since Host carries
/// no display text. Shared by <see cref="IPlaybackHost"/> and <see cref="IRecorderHost"/>'s
/// play/preview members.</summary>
public enum PlaybackErrorCode
{
    EngineNotLive,
    AudioFileMissing,
    MonitorIsCable,
}

/// <summary>A play/preview failure. <see cref="FileName"/> is diagnostic only (set on
/// <see cref="PlaybackErrorCode.AudioFileMissing"/>) — never displayed, so it must never be formatted
/// into operator-facing text; it exists so tests can prove which file was actually attempted.</summary>
public sealed record PlaybackError(PlaybackErrorCode Code, string? FileName = null);

/// <summary>
/// The narrow slice of the host the Board UI needs: the engine state and the phrases to show, plus the
/// actions a board triggers. The ViewModels depend on this (not on the concrete <see cref="EngineHost"/>)
/// so they can be unit-tested with a fake.
/// </summary>
public interface IPlaybackHost
{
    EngineState State { get; }

    /// <summary>Fires on the engine control thread; a UI handler must marshal to its own thread.
    /// Carries the failure reason for error transitions so the UI can show why.</summary>
    event EventHandler<EngineStateChangedEventArgs>? StateChanged;

    /// <summary>The phrase now playing (its id), or null when playback stops. For the playing glow.
    /// Fires off the UI thread; a handler must marshal.</summary>
    event EventHandler<string?>? PlayingPhraseChanged;

    void Start();
    void Stop();
    /// <summary>Stop whatever the operator can currently hear — a phrase playing to the call, and/or
    /// a headphone preview started by <see cref="PreviewEntry"/>/<see cref="PreviewVersion"/>. Backs
    /// the app's single "STOP" button and its hotkey, so both must always be reachable from it.</summary>
    void StopPhrase();
    /// <summary>Play a phrase to the call. When <paramref name="version"/> is given, that take's audio
    /// and gain are used instead of the entry's own — the phrase id used for <see cref="PlayingPhraseChanged"/>
    /// is always the entry's, regardless of which take played. Returns why nothing was played (engine
    /// not Live, or the audio file is missing), or null on success — so a caller can show the drop
    /// instead of it passing silently.</summary>
    PlaybackError? PlayEntry(PhraseEntry entry, PhraseVersion? version = null);
    void EnterOffAir();
    void ExitOffAir();

    /// <summary>Play a catalogued phrase to the monitor (headphones/speakers) so the operator can test
    /// it without the engine running — it never reaches the call. Blocks until playback ends, so callers
    /// should run it off the UI thread. Returns why it failed, or null on success.</summary>
    PlaybackError? PreviewEntry(PhraseEntry entry);

    /// <summary>Like <see cref="PreviewEntry"/> but for one specific version of a phrase, used by the
    /// Edit dialog's version list. Returns why it failed, or null on success.</summary>
    PlaybackError? PreviewVersion(PhraseVersion version);

    /// <summary>Stop a preview started by <see cref="PreviewEntry"/> or <see cref="PreviewVersion"/>
    /// before it finishes on its own. No-op if no preview is active. Safe to call from any thread —
    /// the preview itself blocks a background thread until playback ends or this is called.</summary>
    void StopPreview();
}
