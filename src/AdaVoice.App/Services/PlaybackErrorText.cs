using AdaVoice.App.Resources;
using AdaVoice.Host;

namespace AdaVoice.App.Services;

/// <summary>Maps a <see cref="PlaybackErrorCode"/> to localized text — Host carries only the raw code,
/// never display text. Shared by every view-model that surfaces a play/preview failure as a notice
/// (<c>BoardViewModel</c>, <c>PhraseVersionsViewModel</c>).</summary>
internal static class PlaybackErrorText
{
    public static string Describe(PlaybackError error) => error.Code switch
    {
        // Same message the board already shows when it proactively blocks Play before calling the
        // seam (BoardViewModel's own State != Live guard) — this is that same condition reached via
        // a race instead (state dropped between the guard and the call), so the same text applies.
        PlaybackErrorCode.EngineNotLive => Strings.Board_StartEngineToPlay,
        PlaybackErrorCode.AudioFileMissing => Strings.Board_AudioFileMissing,
        PlaybackErrorCode.MonitorIsCable => Strings.Board_MonitorIsCable,
        _ => "",
    };
}
