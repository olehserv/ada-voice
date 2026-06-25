using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Dsp;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Audio.Playback;

/// <summary>Options that control how phrases play.</summary>
public sealed class PhrasePlayerOptions
{
    /// <summary>Mic gain while a phrase plays (1 = no duck). Default is about -12 dB.</summary>
    public float DuckGain { get; init; } = RampGain.DbToLinear(-12);

    /// <summary>How long the duck and un-duck ramps take.</summary>
    public int DuckRampMs { get; init; } = 50;

    /// <summary>How long the stop fade-out takes.</summary>
    public int StopFadeMs { get; init; } = 10;

    /// <summary>If true, a new trigger replaces the current phrase. If false, it is ignored.</summary>
    public bool ReplaceOnRetrigger { get; init; } = true;
}

/// <summary>
/// Plays phrases into the mixer and ducks the live mic while a phrase plays. Only one
/// phrase plays at a time (single-playback rule, design 06 §1): a new trigger replaces the
/// current phrase by default, or is ignored. When the active phrase ends, the mic un-ducks.
/// </summary>
/// <remarks>
/// Lock order matters here. The mixer raises <c>MixerInputEnded</c> from inside its own
/// read (on the render thread) while holding its internal lock, and our handler then takes
/// <c>_sync</c>. So <see cref="Play"/> and <see cref="Stop"/> never hold <c>_sync</c> while
/// calling the mixer — they take <c>_sync</c> only to update the active phrase, then touch
/// the mixer afterwards. This keeps the two locks from being taken in opposite orders.
/// </remarks>
public sealed class PhrasePlayer : IDisposable
{
    private readonly MixingSampleProvider _mixer;
    private readonly IMicDuck _mic;
    private readonly PhrasePlayerOptions _options;
    private readonly Lock _sync = new();
    private PhraseSampleProvider? _active;

    public PhrasePlayer(MixingSampleProvider mixer, IMicDuck mic, PhrasePlayerOptions? options = null)
    {
        _mixer = mixer;
        _mic = mic;
        _options = options ?? new PhrasePlayerOptions();
        _mixer.MixerInputEnded += OnMixerInputEnded;
    }

    /// <summary>The id of the phrase playing now, or null if none.</summary>
    public string? ActivePhraseId
    {
        get { lock (_sync) { return _active?.Id; } }
    }

    /// <summary>Raised when the active phrase changes: a phrase id when one starts, null when the
    /// active phrase ends. Always raised outside the lock (the mixer raises end on its render thread).</summary>
    public event EventHandler<string?>? ActivePhraseChanged;

    public void Play(Phrase phrase)
    {
        PhraseSampleProvider provider;
        PhraseSampleProvider? toStop = null;

        lock (_sync)
        {
            if (_active is not null)
            {
                if (!_options.ReplaceOnRetrigger)
                    return; // ignore the new trigger

                // Replace: fade out the old phrase. It is no longer the active one, so when
                // its fade ends it will not un-duck (a newer phrase is taking over).
                toStop = _active;
            }

            provider = new PhraseSampleProvider(phrase.Samples, _mixer.WaveFormat, phrase.Id);
            _active = provider;
        }

        toStop?.Stop(_options.StopFadeMs);
        _mixer.AddMixerInput(provider);
        _mic.Duck(_options.DuckGain, _options.DuckRampMs);
        ActivePhraseChanged?.Invoke(this, provider.Id);
    }

    /// <summary>Stop the current phrase with a short fade-out. The mic un-ducks when it ends.</summary>
    public void Stop()
    {
        PhraseSampleProvider? active;
        lock (_sync) { active = _active; }

        active?.Stop(_options.StopFadeMs);
    }

    private void OnMixerInputEnded(object? sender, SampleProviderEventArgs e)
    {
        bool wasActive;
        lock (_sync)
        {
            // Only the active phrase ending should un-duck. A replaced phrase finishing its
            // fade must not un-duck while a newer phrase is still playing.
            wasActive = ReferenceEquals(e.SampleProvider, _active);
            if (wasActive)
                _active = null;
        }

        if (wasActive)
        {
            _mic.Duck(1f, _options.DuckRampMs);
            ActivePhraseChanged?.Invoke(this, null);
        }
    }

    public void Dispose() => _mixer.MixerInputEnded -= OnMixerInputEnded;
}
