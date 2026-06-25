using AdaVoice.Audio.Engine;
using AdaVoice.Core.Domain;

namespace AdaVoice.Host;

/// <summary>
/// The narrow slice of the host the Board UI needs: the engine state and the phrases to show, plus the
/// actions a board triggers. The ViewModels depend on this (not on the concrete <see cref="EngineHost"/>)
/// so they can be unit-tested with a fake.
/// </summary>
public interface IPlaybackHost
{
    EngineState State { get; }
    IReadOnlyList<PhraseEntry> Phrases { get; }

    /// <summary>Fires on the engine control thread; a UI handler must marshal to its own thread.</summary>
    event EventHandler<EngineState>? StateChanged;

    /// <summary>The phrase now playing (its id), or null when playback stops. For the playing glow.
    /// Fires off the UI thread; a handler must marshal.</summary>
    event EventHandler<string?>? PlayingPhraseChanged;

    void Start();
    void Stop();
    void StopPhrase();
    void PlayEntry(PhraseEntry entry);
    void EnterOffAir();
    void ExitOffAir();
}
