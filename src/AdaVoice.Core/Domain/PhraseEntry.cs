namespace AdaVoice.Core.Domain;

/// <summary>
/// Persisted metadata for one phrase (design 04 §1). The audio itself lives in a WAV file named
/// <see cref="FileName"/>; <see cref="GainDb"/> is the loudness-match gain applied at playback (not
/// baked into the file), so re-calibration can recompute it.
/// </summary>
public sealed record PhraseEntry
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string CategoryId { get; init; } = "";
    public string[] Tags { get; init; } = [];
    public string FileName { get; init; } = "";
    public int DurationMs { get; init; }
    public double GainDb { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
