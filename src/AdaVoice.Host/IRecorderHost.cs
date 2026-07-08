using AdaVoice.Audio.Recording;
using AdaVoice.Core.Domain;

namespace AdaVoice.Host;

/// <summary>
/// The recording actions the Board needs, kept behind a seam so the view-model is unit-testable with a
/// fake. <see cref="EngineHost"/> implements it; the methods already exist (the console host uses them).
/// </summary>
public interface IRecorderHost
{
    /// <summary>Go OFF AIR and start recording the mic. False if the engine is not Live.</summary>
    bool TryStartRecording();

    /// <summary>Stop the take, restore the live state, and return the processed result (or null if not
    /// recording).</summary>
    RecordingResult? StopRecording();

    /// <summary>Catalogue a recorded take to disk + metadata, and return the stored entry.</summary>
    PhraseEntry SaveTake(RecordingResult result, string title);

    /// <summary>Catalogue a recorded take as a new version of an existing phrase (not a new phrase).
    /// Returns the updated entry, or null if the phrase id is unknown.</summary>
    PhraseEntry? SaveTakeAsVersion(RecordingResult result, string phraseId, string label);

    /// <summary>Play raw samples to the monitor (to hear a take before saving). Error message, or null.</summary>
    string? Preview(float[] samples, double gainDb);
}
