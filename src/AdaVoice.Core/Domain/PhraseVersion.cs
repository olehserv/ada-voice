namespace AdaVoice.Core.Domain;

/// <summary>
/// An alternate take of a phrase (plan: docs/superpowers/plans/2026-07-07-phrase-versions.md).
/// The audio lives in a WAV file named <see cref="FileName"/>; <see cref="GainDb"/> is that take's own
/// loudness-match gain (each take is calibrated independently, like <see cref="PhraseEntry.GainDb"/>).
/// </summary>
public sealed record PhraseVersion
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string FileName { get; init; } = "";
    public int DurationMs { get; init; }
    public double GainDb { get; init; }
    public DateTime CreatedAt { get; init; }
}
