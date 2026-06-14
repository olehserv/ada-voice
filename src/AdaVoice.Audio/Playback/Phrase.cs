namespace AdaVoice.Audio.Playback;

/// <summary>
/// One phrase, already decoded to float samples in the engine format (48 kHz, mono).
/// Loading and decoding from disk is a storage concern handled in a later phase; the
/// audio core only plays samples it is given.
/// </summary>
public sealed record Phrase(string Id, float[] Samples);
