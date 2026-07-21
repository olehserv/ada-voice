using System.Globalization;
using System.Resources;

namespace AdaVoice.App.Resources;

/// <summary>
/// Strongly typed accessor over Strings.resx (English) plus its uk/pl satellites, so XAML can
/// bind via <c>{x:Static res:Strings.Area_Key}</c>. Hand-written rather than the usual
/// VS-generated Designer.cs: <c>PublicResXFileCodeGenerator</c> is a Visual Studio "single file
/// generator" feature, not something MSBuild's <c>GenerateResource</c> task runs — confirmed by
/// building this project from the CLI, which compiles Strings.resources / uk / pl satellite
/// assemblies correctly but produces no Designer.cs.
///
/// Resolution is keyed off <see cref="CultureInfo.CurrentUICulture"/>, set once at startup in
/// App.xaml.cs (restart-to-apply — see Settings.Language's doc comment) and once in tests by
/// WpfAppFixture. There is no live-switching <c>Culture</c> setter here, unlike the WinForms-era
/// generated class, because this app doesn't need one.
///
/// Add one property per resource key as strings are extracted (Stage 2 of the localization
/// retrofit); keep names as <c>Area_Key</c> to match the .resx key convention.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager ResourceManager =
        new("AdaVoice.App.Resources.Strings", typeof(Strings).Assembly);

    private static string Get(string name) =>
        ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    public static string Main_Record => Get("Main_Record");
    public static string Main_Stop => Get("Main_Stop");
    public static string Main_OffAir => Get("Main_OffAir");
    public static string Main_Setup => Get("Main_Setup");
    public static string Main_RunSetupAgain => Get("Main_RunSetupAgain");
    public static string Main_Settings => Get("Main_Settings");
    public static string Main_SearchPlaceholder => Get("Main_SearchPlaceholder");
    public static string Main_ClearSearch => Get("Main_ClearSearch");
    public static string Main_CategoryFilter => Get("Main_CategoryFilter");
    public static string Main_CategoryFilterTooltip => Get("Main_CategoryFilterTooltip");
    public static string Main_ConversationFilter => Get("Main_ConversationFilter");
    public static string Main_ConversationFilterTooltip => Get("Main_ConversationFilterTooltip");
    public static string Main_TestOnHeadphones => Get("Main_TestOnHeadphones");
    public static string Main_Edit => Get("Main_Edit");
    public static string Main_Versions => Get("Main_Versions");
    public static string Main_Delete => Get("Main_Delete");
    public static string Main_AudioMissing => Get("Main_AudioMissing");
    public static string Main_EmptyBoardTitle => Get("Main_EmptyBoardTitle");
    public static string Main_EmptyBoardHint => Get("Main_EmptyBoardHint");

    /// <summary>Format string ("{0}" = category name) — bind via Binding.StringFormat.</summary>
    public static string Main_CategoryEmptyTitle => Get("Main_CategoryEmptyTitle");

    /// <summary>Format string ("{0}" = category name) — bind via Binding.StringFormat.</summary>
    public static string Main_RecordInto => Get("Main_RecordInto");

    public static string Main_NoMatchCheckedCategories => Get("Main_NoMatchCheckedCategories");

    /// <summary>Format string ("{0}" = search text) — bind via Binding.StringFormat.</summary>
    public static string Main_SearchNoMatchTitle => Get("Main_SearchNoMatchTitle");

    /// <summary>Format string ("{0}" = conversation name) — bind via Binding.StringFormat.</summary>
    public static string Main_ConversationEmptyTitle => Get("Main_ConversationEmptyTitle");

    public static string Main_AddPhrasesFromConversations => Get("Main_AddPhrasesFromConversations");

    public static string Settings_Title => Get("Settings_Title");
    public static string Settings_Appearance => Get("Settings_Appearance");
    public static string Settings_Theme => Get("Settings_Theme");
    public static string Settings_ThemeFollowSystem => Get("Settings_ThemeFollowSystem");
    public static string Settings_ThemeLight => Get("Settings_ThemeLight");
    public static string Settings_ThemeDark => Get("Settings_ThemeDark");
    public static string Settings_Levels => Get("Settings_Levels");
    public static string Settings_MicDuck => Get("Settings_MicDuck");
    public static string Settings_Behavior => Get("Settings_Behavior");
    public static string Settings_AlwaysOnTop => Get("Settings_AlwaysOnTop");
    public static string Settings_ReplaceOnRetrigger => Get("Settings_ReplaceOnRetrigger");
    public static string Settings_AppliesAfterRestart => Get("Settings_AppliesAfterRestart");
    public static string Settings_LanguageAndBackup => Get("Settings_LanguageAndBackup");
    public static string Settings_Language => Get("Settings_Language");
    public static string Settings_Export => Get("Settings_Export");
    public static string Settings_Import => Get("Settings_Import");
    public static string Settings_OpenBackupFolder => Get("Settings_OpenBackupFolder");
    public static string Settings_LastBackupLabel => Get("Settings_LastBackupLabel");
    public static string Settings_LastBackupNever => Get("Settings_LastBackupNever");
    public static string Settings_Done => Get("Settings_Done");

    public static string ManageConversations_Title => Get("ManageConversations_Title");
    public static string ManageConversations_NoConversationsYet => Get("ManageConversations_NoConversationsYet");
    public static string ManageConversations_NewNamePlaceholder => Get("ManageConversations_NewNamePlaceholder");
    public static string ManageConversations_AddConversation => Get("ManageConversations_AddConversation");
    public static string ManageConversations_Delete => Get("ManageConversations_Delete");
    public static string ManageConversations_PlayRandomVersion => Get("ManageConversations_PlayRandomVersion");
    public static string ManageConversations_MoveUp => Get("ManageConversations_MoveUp");
    public static string ManageConversations_MoveDown => Get("ManageConversations_MoveDown");
    public static string ManageConversations_Remove => Get("ManageConversations_Remove");
    public static string ManageConversations_AddPhrase => Get("ManageConversations_AddPhrase");
    public static string ManageConversations_Done => Get("ManageConversations_Done");

    public static string ManageCategories_Title => Get("ManageCategories_Title");
    public static string ManageCategories_Delete => Get("ManageCategories_Delete");
    public static string ManageCategories_NewNamePlaceholder => Get("ManageCategories_NewNamePlaceholder");
    public static string ManageCategories_AddCategory => Get("ManageCategories_AddCategory");
    public static string ManageCategories_Done => Get("ManageCategories_Done");

    public static string PhraseEdit_Title => Get("PhraseEdit_Title");
    public static string PhraseEdit_TitleLabel => Get("PhraseEdit_TitleLabel");
    public static string PhraseEdit_CategoryLabel => Get("PhraseEdit_CategoryLabel");
    public static string PhraseEdit_TagsLabel => Get("PhraseEdit_TagsLabel");
    public static string PhraseEdit_RemoveTag => Get("PhraseEdit_RemoveTag");
    public static string PhraseEdit_AddTagPlaceholder => Get("PhraseEdit_AddTagPlaceholder");
    public static string PhraseEdit_Add => Get("PhraseEdit_Add");
    public static string PhraseEdit_AddExistingTag => Get("PhraseEdit_AddExistingTag");
    public static string PhraseEdit_Cancel => Get("PhraseEdit_Cancel");
    public static string PhraseEdit_Save => Get("PhraseEdit_Save");

    public static string RepairPhrase_Title => Get("RepairPhrase_Title");
    public static string RepairPhrase_AudioMissing => Get("RepairPhrase_AudioMissing");
    public static string RepairPhrase_Cancel => Get("RepairPhrase_Cancel");
    public static string RepairPhrase_Remove => Get("RepairPhrase_Remove");
    public static string RepairPhrase_ReRecord => Get("RepairPhrase_ReRecord");

    /// <summary>Format string ("{0}" = phrase title) — bind via Binding.StringFormat.</summary>
    public static string PhraseVersions_TitleFormat => Get("PhraseVersions_TitleFormat");
    public static string PhraseVersions_AudioMissing => Get("PhraseVersions_AudioMissing");
    public static string PhraseVersions_Play => Get("PhraseVersions_Play");
    public static string PhraseVersions_Stop => Get("PhraseVersions_Stop");
    public static string PhraseVersions_Delete => Get("PhraseVersions_Delete");
    public static string PhraseVersions_AddVersion => Get("PhraseVersions_AddVersion");
    public static string PhraseVersions_Close => Get("PhraseVersions_Close");

    public static string Recorder_Title => Get("Recorder_Title");
    public static string Recorder_Record => Get("Recorder_Record");
    public static string Recorder_IdleGuidance => Get("Recorder_IdleGuidance");
    public static string Recorder_RecordingInProgress => Get("Recorder_RecordingInProgress");
    public static string Recorder_Stop => Get("Recorder_Stop");
    public static string Recorder_Processing => Get("Recorder_Processing");
    public static string Recorder_TitleLabel => Get("Recorder_TitleLabel");
    public static string Recorder_Discard => Get("Recorder_Discard");
    public static string Recorder_Preview => Get("Recorder_Preview");
    public static string Recorder_Save => Get("Recorder_Save");
    public static string Recorder_OffAirHint => Get("Recorder_OffAirHint");
    public static string Recorder_Close => Get("Recorder_Close");

    public static string Wizard_Title => Get("Wizard_Title");
    public static string Wizard_Cancel => Get("Wizard_Cancel");
    public static string Wizard_Back => Get("Wizard_Back");
    public static string Wizard_SkipAnyway => Get("Wizard_SkipAnyway");

    public static string EnvChecks_Title => Get("EnvChecks_Title");
    public static string EnvChecks_Checking => Get("EnvChecks_Checking");
    public static string EnvChecks_DownloadVbCable => Get("EnvChecks_DownloadVbCable");
    public static string EnvChecks_Recheck => Get("EnvChecks_Recheck");

    public static string Calibration_Title => Get("Calibration_Title");
    public static string Calibration_Instructions => Get("Calibration_Instructions");
    public static string Calibration_Start => Get("Calibration_Start");
    public static string Calibration_RecordingInProgress => Get("Calibration_RecordingInProgress");
    public static string Calibration_Captured => Get("Calibration_Captured");
    public static string Calibration_TryAgain => Get("Calibration_TryAgain");
    public static string Calibration_TooQuiet => Get("Calibration_TooQuiet");
    public static string Calibration_AlreadyRecording => Get("Calibration_AlreadyRecording");
    public static string Calibration_CouldNotPauseCallFeed => Get("Calibration_CouldNotPauseCallFeed");

    public static string HotkeyStatus_Title => Get("HotkeyStatus_Title");
    public static string Instruction_Title => Get("Instruction_Title");
    public static string FirstCall_Title => Get("FirstCall_Title");
    public static string FirstCall_Subtitle => Get("FirstCall_Subtitle");

    public static string Board_NoneConversation => Get("Board_NoneConversation");
    public static string Board_CategoriesLabel => Get("Board_CategoriesLabel");

    /// <summary>Format string ("{0}" = count) — use with string.Format.</summary>
    public static string Board_CategoriesCountFormat => Get("Board_CategoriesCountFormat");

    public static string Board_ConversationsLabel => Get("Board_ConversationsLabel");
    public static string Board_StartEngineToPlay => Get("Board_StartEngineToPlay");

    /// <summary>Format string ("{0}" = title, "{1}" = error) — use with string.Format.</summary>
    public static string Board_CouldNotPlayFormat => Get("Board_CouldNotPlayFormat");

    public static string Board_PreviewPlaybackError => Get("Board_PreviewPlaybackError");
    public static string Board_StartEngineToRecord => Get("Board_StartEngineToRecord");
    public static string Board_PressStartToRecord => Get("Board_PressStartToRecord");
    public static string Board_RecordingStartError => Get("Board_RecordingStartError");

    /// <summary>Format string ("{0}" = timestamp) — use with string.Format.</summary>
    public static string Board_DefaultTakeTitleFormat => Get("Board_DefaultTakeTitleFormat");

    public static string Board_NoSignal => Get("Board_NoSignal");
    public static string Board_RecordingFinishError => Get("Board_RecordingFinishError");
    public static string Board_Previewing => Get("Board_Previewing");
    public static string Board_SaveRecordingError => Get("Board_SaveRecordingError");
    public static string Board_SavedButApplyFailed => Get("Board_SavedButApplyFailed");
    public static string Board_SaveVersionError => Get("Board_SaveVersionError");
    public static string Board_SaveVersionPhraseGone => Get("Board_SaveVersionPhraseGone");
    public static string Board_NewVersionSaved => Get("Board_NewVersionSaved");
    public static string Board_TakeDiscarded => Get("Board_TakeDiscarded");
    public static string Board_AudioFileMissing => Get("Board_AudioFileMissing");
    public static string Board_MonitorIsCable => Get("Board_MonitorIsCable");
    public static string Board_LibraryReadError => Get("Board_LibraryReadError");
    public static string Board_LibraryCorrupt => Get("Board_LibraryCorrupt");
    public static string Board_LibraryRecovered => Get("Board_LibraryRecovered");
    public static string Board_SettingsWereReset => Get("Board_SettingsWereReset");

    public static string Status_Live => Get("Status_Live");
    public static string Status_OffAir => Get("Status_OffAir");
    public static string Status_Degraded => Get("Status_Degraded");
    public static string Status_Stopped => Get("Status_Stopped");
    public static string Status_DeviceChanged => Get("Status_DeviceChanged");
    public static string Status_CableStalled => Get("Status_CableStalled");

    /// <summary>Format string ("{0}" = channel count) — use with string.Format.</summary>
    public static string Status_TooManyMicChannelsFormat => Get("Status_TooManyMicChannelsFormat");
    public static string Status_CableSampleRateMismatch => Get("Status_CableSampleRateMismatch");

    /// <summary>Format string ("{0}" = current step, "{1}" = total steps) — use with string.Format.</summary>
    public static string Wizard_StepLabelFormat => Get("Wizard_StepLabelFormat");

    public static string Wizard_Finish => Get("Wizard_Finish");
    public static string Wizard_Next => Get("Wizard_Next");

    /// <summary>Format string ("{0}" = hotkey name) — use with string.Format.</summary>
    public static string Hotkey_Registered => Get("Hotkey_Registered");

    /// <summary>Format string ("{0}" = hotkey name) — use with string.Format.</summary>
    public static string Hotkey_Status => Get("Hotkey_Status");

    public static string Hotkey_Unavailable => Get("Hotkey_Unavailable");

    public static string Instruction_Step1 => Get("Instruction_Step1");
    public static string Instruction_Step2 => Get("Instruction_Step2");
    public static string Instruction_Step3 => Get("Instruction_Step3");
    public static string Instruction_Step4 => Get("Instruction_Step4");

    public static string FirstCall_Check1 => Get("FirstCall_Check1");
    public static string FirstCall_Check2 => Get("FirstCall_Check2");
    public static string FirstCall_Check3 => Get("FirstCall_Check3");

    public static string Calibration_MicAccessError => Get("Calibration_MicAccessError");

    public static string PhraseVersions_PrimaryLabel => Get("PhraseVersions_PrimaryLabel");

    /// <summary>Format string ("{0}" = dropped-version count) — use with string.Format.</summary>
    public static string Backup_ExportDroppedVersionsFormat => Get("Backup_ExportDroppedVersionsFormat");

    /// <summary>Format string ("{0}" = error message) — use with string.Format.</summary>
    public static string Backup_ExportErrorFormat => Get("Backup_ExportErrorFormat");

    /// <summary>Format string ("{0}" = error message) — use with string.Format.</summary>
    public static string Backup_ImportErrorFormat => Get("Backup_ImportErrorFormat");

    /// <summary>Format string ("{0}" = added count, "{1}" = skipped count) — use with string.Format.</summary>
    public static string Backup_ImportSuccessFormat => Get("Backup_ImportSuccessFormat");
    public static string Backup_ImportArchiveOpenFailedFormat => Get("Backup_ImportArchiveOpenFailedFormat");
    public static string Backup_ImportTooManyEntriesFormat => Get("Backup_ImportTooManyEntriesFormat");
    public static string Backup_ImportLibraryJsonTooLarge => Get("Backup_ImportLibraryJsonTooLarge");
    public static string Backup_ImportNoValidLibraryJson => Get("Backup_ImportNoValidLibraryJson");
    public static string Backup_ImportUnsupportedVersionFormat => Get("Backup_ImportUnsupportedVersionFormat");
    public static string Backup_ImportFailedFormat => Get("Backup_ImportFailedFormat");
    public static string Backup_ImportAudioEntryTooLarge => Get("Backup_ImportAudioEntryTooLarge");
    public static string Backup_ImportTotalAudioTooLarge => Get("Backup_ImportTotalAudioTooLarge");

    public static string DialogPrompts_Cancel => Get("DialogPrompts_Cancel");
    public static string DialogPrompts_Ok => Get("DialogPrompts_Ok");

    public static string EnvChecks_Pass => Get("EnvChecks_Pass");
    public static string EnvChecks_Fail => Get("EnvChecks_Fail");
    public static string EnvChecks_CableOutputName => Get("EnvChecks_CableOutputName");
    public static string EnvChecks_CableSampleRateName => Get("EnvChecks_CableSampleRateName");
    public static string EnvChecks_DefaultOutputName => Get("EnvChecks_DefaultOutputName");
    public static string EnvChecks_MicrophoneName => Get("EnvChecks_MicrophoneName");
    public static string EnvChecks_CableOutputMissingFormat => Get("EnvChecks_CableOutputMissingFormat");
    public static string EnvChecks_CableSampleRateNoCable => Get("EnvChecks_CableSampleRateNoCable");
    public static string EnvChecks_CableSampleRateWrongFormat => Get("EnvChecks_CableSampleRateWrongFormat");
    public static string EnvChecks_CableSampleRatePass => Get("EnvChecks_CableSampleRatePass");
    public static string EnvChecks_DefaultOutputIsCableFormat => Get("EnvChecks_DefaultOutputIsCableFormat");
    public static string EnvChecks_NoneFound => Get("EnvChecks_NoneFound");
    public static string EnvChecks_MicrophoneNoneFound => Get("EnvChecks_MicrophoneNoneFound");
    public static string EnvChecks_MicrophoneNotFoundFormat => Get("EnvChecks_MicrophoneNotFoundFormat");
    public static string Category_Uncategorized => Get("Category_Uncategorized");

    public static string Main_DeletePhraseTitle => Get("Main_DeletePhraseTitle");

    /// <summary>Format string ("{0}" = phrase title) — use with string.Format.</summary>
    public static string Main_DeletePhraseConfirmFormat => Get("Main_DeletePhraseConfirmFormat");

    public static string Main_ManageCategoriesMenuItem => Get("Main_ManageCategoriesMenuItem");
    public static string Main_ManageConversationsMenuItem => Get("Main_ManageConversationsMenuItem");
    public static string Main_ExportFilter => Get("Main_ExportFilter");

    /// <summary>Format string ("{0}" = hotkey name) — use with string.Format.</summary>
    public static string Main_HotkeyHintFormat => Get("Main_HotkeyHintFormat");

    public static string Main_UseOnScreenStop => Get("Main_UseOnScreenStop");
    public static string Main_HotkeyUnavailableTitle => Get("Main_HotkeyUnavailableTitle");
    public static string Main_SavedToastTitle => Get("Main_SavedToastTitle");
    public static string Main_DeletedToastTitle => Get("Main_DeletedToastTitle");

    public static string App_AlreadyRunning => Get("App_AlreadyRunning");

    /// <summary>Format string ("{0}" = exception message) — use with string.Format.</summary>
    public static string App_CrashMessageFormat => Get("App_CrashMessageFormat");

    public static string Settings_ImportLibraryTitle => Get("Settings_ImportLibraryTitle");
    public static string Settings_ImportLibraryPrompt => Get("Settings_ImportLibraryPrompt");
    public static string Settings_Merge => Get("Settings_Merge");
    public static string Settings_Replace => Get("Settings_Replace");
    public static string Settings_RestartRequiredTitle => Get("Settings_RestartRequiredTitle");
    public static string Settings_RestartPrompt => Get("Settings_RestartPrompt");
    public static string Settings_RestartNow => Get("Settings_RestartNow");

    public static string ManageCategories_DeleteConfirmTitle => Get("ManageCategories_DeleteConfirmTitle");

    /// <summary>Format string ("{0}" = category name) — use with string.Format.</summary>
    public static string ManageCategories_DeleteConfirmFormat => Get("ManageCategories_DeleteConfirmFormat");

    public static string ManageConversations_DeleteConfirmTitle => Get("ManageConversations_DeleteConfirmTitle");

    /// <summary>Format string ("{0}" = conversation name) — use with string.Format.</summary>
    public static string ManageConversations_DeleteConfirmFormat => Get("ManageConversations_DeleteConfirmFormat");

    public static string PhraseVersions_DeleteConfirmTitle => Get("PhraseVersions_DeleteConfirmTitle");

    /// <summary>Format string ("{0}" = version label) — use with string.Format.</summary>
    public static string PhraseVersions_DeleteConfirmFormat => Get("PhraseVersions_DeleteConfirmFormat");

    public static string Recorder_DiscardConfirmTitle => Get("Recorder_DiscardConfirmTitle");
    public static string Recorder_DiscardConfirmMessage => Get("Recorder_DiscardConfirmMessage");
}
