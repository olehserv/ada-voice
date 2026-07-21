using AdaVoice.App.Resources;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;
using AdaVoice.Host;

namespace AdaVoice.App.Tests;

public class BoardViewModelTests
{
    private static BoardViewModel NewBoard(
        FakePlaybackHost host,
        Func<PhraseItemViewModel, Task<bool>>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<PhraseVersionsViewModel>? showVersionsDialog = null,
        Action<ILibraryHost>? showManageCategories = null,
        Action<ILibraryHost>? showManageConversations = null,
        Action<SetupWizardViewModel>? showSetupWizard = null,
        ISettingsHost? settingsHost = null,
        Action<ISettingsHost, ISetupHost, string?, Func<string?>>? showSettings = null,
        Func<RepairPhraseViewModel, bool>? showRepairDialog = null,
        Action? showRecorder = null,
        Random? rng = null) =>
        new(host, host, host, host, settingsHost ?? new FakeSettingsHost(), new StatusViewModel(host),
            new SettingsViewModel(new FakeSettingsHost()),
            getActiveHotkey: () => "Pause", confirmDelete: confirmDelete, showEditDialog: showEditDialog,
            showVersionsDialog: showVersionsDialog,
            showManageCategories: showManageCategories, showManageConversations: showManageConversations,
            showSetupWizard: showSetupWizard, showSettings: showSettings, showRepairDialog: showRepairDialog,
            showRecorder: showRecorder, rng: rng);

    private static RecordingResult Take() => new(new float[10], GainDb: -3, DurationMs: 1000, PeakDbfs: -6);

    /// <summary>A <see cref="Random"/> that always returns a fixed index — deterministic version-pick
    /// tests without depending on a real seed's output (which the .NET RNG algorithm gives no
    /// guarantees about across versions).</summary>
    private sealed class FixedRandom(int value) : Random
    {
        public override int Next(int maxValue) => value;
    }

    [Fact]
    public async Task Starting_a_recording_opens_the_recorder_window()
    {
        var host = new FakePlaybackHost { State = EngineState.Live };
        var opened = false;
        var board = NewBoard(host, showRecorder: () => opened = true);

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.True(opened);
    }

    /// <summary>The real showRecorder callback (MainWindow.ShowRecorder) blocks on ShowDialog until
    /// the recorder window closes, so StartRecordingCommand's Task is still "running" for the whole
    /// time the callback is on the stack — exactly where this assertion runs. Without
    /// AllowConcurrentExecutions on that command, CanExecute would be false here, and the Record
    /// button inside the dialog would appear permanently disabled.</summary>
    [Fact]
    public async Task Record_command_stays_executable_while_the_recorder_window_is_open()
    {
        var host = new FakePlaybackHost { State = EngineState.Live };
        BoardViewModel? board = null;
        bool? canExecuteWhileDialogIsOpen = null;
        board = NewBoard(host, showRecorder: () => canExecuteWhileDialogIsOpen = board!.StartRecordingCommand.CanExecute(null));

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.True(canExecuteWhileDialogIsOpen);
    }

    [Fact]
    public async Task Play_when_not_live_raises_a_warning_notification()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Stopped,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
        };
        var board = NewBoard(host);
        BoardNotification? seen = null;
        board.Notified += (_, n) => seen = n;

        await board.PlayCommand.ExecuteAsync(board.Phrases[0]);

        Assert.NotNull(seen);
        Assert.Equal(NoticeSeverity.Warning, seen.Severity);
        Assert.Equal(board.Notice, seen.Message); // the property still mirrors the last message
    }

    [Fact]
    public async Task Record_with_a_take_already_pending_reopens_the_recorder_instead_of_overwriting_it()
    {
        var host = new FakePlaybackHost { State = EngineState.Live, NextStopResult = Take() };
        var opens = 0;
        var board = NewBoard(host, showRecorder: () => opens++);
        await board.StartRecordingCommand.ExecuteAsync(null);
        await board.StopRecordingCommand.ExecuteAsync(null); // take is now waiting to be saved

        await board.StartRecordingCommand.ExecuteAsync(null); // Record clicked again on the Board

        Assert.False(board.IsRecording);                      // no new take was started…
        Assert.True(board.HasPendingTake);                    // …the waiting one survived…
        Assert.Equal(2, opens);                               // …and the recorder window reopened
        Assert.Equal(1, host.Calls.Count(c => c == "TryStartRecording"));
    }

    [Fact]
    public void A_settings_warning_shows_on_the_board_at_startup()
    {
        var host = new FakePlaybackHost(); // no library warning
        var settingsHost = new FakeSettingsHost { SettingsWereReset = true };

        var board = NewBoard(host, settingsHost: settingsHost);

        Assert.Equal(Strings.Board_SettingsWereReset, board.Notice);
    }

    [Fact]
    public void A_library_warning_takes_priority_over_a_settings_warning()
    {
        var host = new FakePlaybackHost { LoadStatus = LibraryLoadStatus.ReadError };
        var settingsHost = new FakeSettingsHost { SettingsWereReset = true };

        var board = NewBoard(host, settingsHost: settingsHost);

        Assert.Equal(Strings.Board_LibraryReadError, board.Notice);
    }

    [Fact]
    public async Task Recording_while_stopped_warns_without_opening_the_recorder_window()
    {
        var host = new FakePlaybackHost(); // State defaults to Stopped
        var opened = false;
        var board = NewBoard(host, showRecorder: () => opened = true);

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.False(opened);
        Assert.False(board.IsRecording);
        Assert.NotNull(board.Notice);
    }

    [Fact]
    public async Task A_failed_recording_start_while_running_still_opens_the_recorder_window_to_show_why()
    {
        var host = new FakePlaybackHost { State = EngineState.Live, CanRecord = false }; // e.g. mic busy
        var opened = false;
        var board = NewBoard(host, showRecorder: () => opened = true);

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.True(opened);
        Assert.False(board.IsRecording);
    }

    [Fact]
    public void Phrases_come_from_the_host()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] };

        Assert.Single(NewBoard(host).Phrases);
    }

    [Fact]
    public void Play_command_plays_that_phrase_to_the_call_when_live()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
        };
        var board = NewBoard(host);
        var item = board.Phrases[0];

        board.PlayCommand.Execute(item);

        Assert.Equal("PlayEntry", Assert.Single(host.Calls));
        Assert.Same(item.Entry, host.PlayedEntry);
    }

    // ---- Phrase versions: random pick during a Conversation step ------------------------------

    [Fact]
    public void Board_click_always_plays_the_primary_even_when_the_phrase_has_versions()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases =
            [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav", Versions = [new PhraseVersion { Id = "pv-1" }] },
            ],
        };
        var board = NewBoard(host);

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.Null(host.PlayedVersion); // no Conversation active — always the primary
    }

    [Fact]
    public void Conversation_step_with_the_random_flag_off_always_plays_the_primary()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases =
            [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav", Versions = [new PhraseVersion { Id = "pv-1" }] },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"], UseRandomVersion = false }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.Null(host.PlayedVersion); // default off — same as today's behavior, unchanged
    }

    [Fact]
    public void Conversation_step_with_the_random_flag_on_can_pick_the_primary()
    {
        var version = new PhraseVersion { Id = "pv-1", FileName = "p-1-pv-1.wav" };
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav", Versions = [version] }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"], UseRandomVersion = true }],
        };
        // Candidates are [primary(null), version] — index 0 is the primary.
        var board = NewBoard(host, rng: new FixedRandom(0));
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.Null(host.PlayedVersion);
    }

    [Fact]
    public void Conversation_step_with_the_random_flag_on_can_pick_a_version()
    {
        var version = new PhraseVersion { Id = "pv-1", FileName = "p-1-pv-1.wav" };
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav", Versions = [version] }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"], UseRandomVersion = true }],
        };
        // Candidates are [primary(null), version] — index 1 is the version. Proves the pool really is
        // primary + all versions, not just the versions.
        var board = NewBoard(host, rng: new FixedRandom(1));
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.Same(version, host.PlayedVersion);
    }

    [Fact]
    public void Conversation_step_with_the_random_flag_on_and_no_versions_always_plays_the_primary()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }], // no versions
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"], UseRandomVersion = true }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.Null(host.PlayedVersion);
    }

    /// <summary>Review finding 1: a random pick must never land on a version whose audio file is
    /// missing — that would play silence into a live call. <c>FixedRandom(1)</c> always returns index
    /// 1, whatever the pool: with the broken version excluded, the pool is [primary, pv-2] and index 1
    /// is the healthy version. Before the fix, the unfiltered pool was [primary, pv-1, pv-2] and index
    /// 1 was the broken one — this test fails on that code.</summary>
    [Fact]
    public void Random_version_pick_never_plays_a_broken_version()
    {
        var broken = new PhraseVersion { Id = "pv-1", FileName = "p-1-pv-1.wav" };
        var healthy = new PhraseVersion { Id = "pv-2", FileName = "p-1-pv-2.wav" };
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav", Versions = [broken, healthy] }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"], UseRandomVersion = true }],
            BrokenVersionIds = ["pv-1"],
        };
        var board = NewBoard(host, rng: new FixedRandom(1));
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.Same(healthy, host.PlayedVersion);
    }

    [Fact]
    public void Random_version_pick_falls_back_to_the_primary_when_the_only_version_is_broken()
    {
        var broken = new PhraseVersion { Id = "pv-1", FileName = "p-1-pv-1.wav" };
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav", Versions = [broken] }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"], UseRandomVersion = true }],
            BrokenVersionIds = ["pv-1"],
        };
        var board = NewBoard(host, rng: new FixedRandom(0));
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.Null(host.PlayedVersion);
    }

    /// <summary>Review finding 1: if the host reports playback didn't actually happen (e.g. the file
    /// went missing after the board loaded), the board must show it — and must not move the
    /// conversation's step pointer as if the phrase had played.</summary>
    [Fact]
    public void A_play_error_from_the_host_shows_a_toast_and_does_not_advance_the_conversation_step()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav" },
                new PhraseEntry { Id = "p-2", FileName = "p-2.wav" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1", "p-2"] }],
            PlayEntryResult = new PlaybackError(PlaybackErrorCode.AudioFileMissing),
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");
        BoardNotification? seen = null;
        board.Notified += (_, n) => seen = n;

        board.PlayCommand.Execute(board.Phrases.Single(p => p.Entry.Id == "p-1"));

        Assert.NotNull(seen);
        Assert.Equal(NoticeSeverity.Error, seen.Severity);
        Assert.Contains(Strings.Board_AudioFileMissing, seen.Message);
        Assert.True(board.Phrases.Single(p => p.Entry.Id == "p-1").IsCurrentStep); // pointer did not move
    }

    [Fact]
    public void Play_when_not_live_shows_a_notice_and_does_not_play()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Stopped,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
        };
        var board = NewBoard(host);

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.DoesNotContain("PlayEntry", host.Calls);
        Assert.NotNull(board.Notice);
    }

    [Fact]
    public void Play_a_broken_phrase_opens_the_repair_dialog_instead_of_a_notice()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", FileName = "p-1.wav" }],
            BrokenPhraseIds = ["p-1"],
        };
        RepairPhraseViewModel? shown = null;
        var board = NewBoard(host, showRepairDialog: repair => { shown = repair; return false; }); // cancelled

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.DoesNotContain("PlayEntry", host.Calls);
        Assert.NotNull(shown);
        Assert.Equal("Hi", shown!.Title);
        Assert.Single(board.Phrases); // cancelled — nothing removed
    }

    [Fact]
    public void Repair_dialog_remove_deletes_the_broken_phrase()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi" }],
            BrokenPhraseIds = ["p-1"],
        };
        string? deleted = null;
        var board = NewBoard(host, showRepairDialog: repair => { repair.ChooseRemove(); return true; });
        board.Deleted += (_, title) => deleted = title;

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.Empty(board.Phrases);
        Assert.Equal("Hi", deleted);
    }

    [Fact]
    public void Repair_dialog_re_record_removes_the_old_entry_and_starts_recording_prefilled()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live, // recording needs the engine running
            CanRecord = true,
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-2", Tags = ["urgent"] }],
            BrokenPhraseIds = ["p-1"],
            NextStopResult = Take(),
        };
        var board = NewBoard(host, showRepairDialog: repair => { repair.ChooseReRecord(); return true; });

        // Block instead of `await`: awaiting here would resume this test method's continuation on
        // whatever threadpool thread completed StartRecording's Task.Run (no SynchronizationContext
        // in this headless xunit host) — and the SaveTake below touches the Phrases ObservableCollection,
        // which WPF's CollectionView ties to the thread that first created it (here, this test's own
        // thread). GetAwaiter().GetResult() waits for the same full completion but keeps running on
        // this thread, avoiding the "different thread" CollectionView exception (same hazard the
        // Save/RecordIntoCategory tests above avoid by using .Execute() instead of awaiting). No
        // deadlock risk: nothing downstream marshals back to this (blocked) thread.
#pragma warning disable xUnit1031
        board.PlayCommand.ExecuteAsync(board.Phrases[0]).GetAwaiter().GetResult();

        Assert.Empty(board.Phrases); // old broken entry removed
        Assert.True(board.IsRecording);

        // Go through the real Stop path (not a direct PendingTake assignment) so this test would
        // actually catch a regression where StopRecording overwrites the pre-filled title.
        board.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
#pragma warning restore xUnit1031

        Assert.Equal("Hi", board.NewTitle); // pre-filled from the broken entry, preserved through Stop

        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Hi");
        Assert.Equal("c-2", saved.CategoryId);
        Assert.Equal(["urgent"], saved.Tags);
    }

    // Dedicated, minimal reproduction of the same bug: the repair dialog's Re-record path must
    // survive the real Stop path, not just a directly-assigned PendingTake (which would mask the
    // bug, since StopRecording is the only place that actually sets NewTitle in production).
    [Fact]
    public void Repair_dialog_re_record_title_survives_the_real_stop_recording_path()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live, // recording needs the engine running
            CanRecord = true,
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi" }],
            BrokenPhraseIds = ["p-1"],
            NextStopResult = Take(),
        };
        var board = NewBoard(host, showRepairDialog: repair => { repair.ChooseReRecord(); return true; });

#pragma warning disable xUnit1031
        board.PlayCommand.ExecuteAsync(board.Phrases[0]).GetAwaiter().GetResult();
        board.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
#pragma warning restore xUnit1031

        Assert.Equal("Hi", board.NewTitle); // not overwritten with a "Take <timestamp>" default

        board.SaveTakeCommand.Execute(null);

        Assert.Contains(host.Phrases, p => p.Title == "Hi");
    }

    [Fact]
    public async Task Test_on_headphones_previews_the_entry_off_the_call()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Stopped, // works even with the engine stopped
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
        };
        var board = NewBoard(host);
        var item = board.Phrases[0];

        await board.TestOnHeadphonesCommand.ExecuteAsync(item);

        Assert.Contains("PreviewEntry", host.Calls);
        Assert.Same(item.Entry, host.PreviewedEntry);
        Assert.DoesNotContain("PlayEntry", host.Calls); // never toward the call
    }

    [Fact]
    public async Task Test_on_headphones_surfaces_a_preview_error_as_a_notice()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
            PreviewEntryResult = new PlaybackError(PlaybackErrorCode.AudioFileMissing),
        };
        var board = NewBoard(host);

        await board.TestOnHeadphonesCommand.ExecuteAsync(board.Phrases[0]);

        Assert.Equal(Strings.Board_AudioFileMissing, board.Notice);
    }

    [Fact]
    public async Task Test_on_headphones_enables_stop_while_previewing_and_clears_it_after()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
        };
        var board = NewBoard(host);
        var canStopDuringPreview = false;
        host.OnPreviewing = () => canStopDuringPreview = board.CanStop;

        await board.TestOnHeadphonesCommand.ExecuteAsync(board.Phrases[0]);

        Assert.True(canStopDuringPreview); // the STOP button was reachable while it was playing
        Assert.False(board.CanStop); // and disabled again once the preview finished
    }

    [Fact]
    public void Playing_phrase_changed_highlights_only_that_item()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1" }, new PhraseEntry { Id = "p-2" }],
        };
        var board = NewBoard(host);

        host.RaisePlayingPhraseChanged("p-2");

        Assert.False(board.Phrases[0].IsPlaying);
        Assert.True(board.Phrases[1].IsPlaying);
    }

    [Fact]
    public void Playing_phrase_null_clears_every_highlight()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] };
        var board = NewBoard(host);
        host.RaisePlayingPhraseChanged("p-1");

        host.RaisePlayingPhraseChanged(null);

        Assert.All(board.Phrases, i => Assert.False(i.IsPlaying));
    }

    [Fact]
    public void Stop_command_stops_the_current_phrase()
    {
        var host = new FakePlaybackHost();

        NewBoard(host).StopCommand.Execute(null);

        Assert.Equal("StopPhrase", Assert.Single(host.Calls));
    }

    [Fact]
    public void Start_and_stop_engine_commands_drive_the_host()
    {
        var host = new FakePlaybackHost();
        var board = NewBoard(host);

        board.StartEngineCommand.Execute(null);
        board.StopEngineCommand.Execute(null);

        Assert.Equal(["Start", "Stop"], host.Calls);
    }

    [Fact]
    public void Off_air_toggle_enters_then_exits()
    {
        var host = new FakePlaybackHost { State = EngineState.Live };
        var board = NewBoard(host);

        board.ToggleOffAirCommand.Execute(null); // Live -> enter OFF AIR
        host.State = EngineState.OffAir;
        board.ToggleOffAirCommand.Execute(null); // OFF AIR -> exit

        Assert.Equal(["EnterOffAir", "ExitOffAir"], host.Calls);
    }

    [Fact]
    public async Task Start_recording_enters_recording_when_the_host_allows_it()
    {
        var board = NewBoard(new FakePlaybackHost { State = EngineState.Live, CanRecord = true });

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.True(board.IsRecording);
    }

    [Fact]
    public async Task Start_recording_shows_a_notice_when_not_live()
    {
        var board = NewBoard(new FakePlaybackHost { CanRecord = false });

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.False(board.IsRecording);
        Assert.NotNull(board.Notice);
    }

    // The host can throw (e.g. the mic vanished between OFF AIR and opening the capture) — the
    // command must surface a notice, never crash the app.
    [Fact]
    public async Task Start_recording_failure_becomes_a_notice()
    {
        var board = NewBoard(new FakePlaybackHost { State = EngineState.Live, TryStartRecordingThrows = true });

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.False(board.IsRecording);
        Assert.NotNull(board.Notice);
    }

    [Fact]
    public async Task Stop_recording_with_signal_holds_a_pending_take()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);

        await board.StopRecordingCommand.ExecuteAsync(null);

        Assert.True(board.HasPendingTake);
        Assert.False(board.IsRecording);
    }

    [Fact]
    public async Task Stop_recording_with_no_signal_keeps_nothing_and_notices()
    {
        var host = new FakePlaybackHost { NextStopResult = RecordingResult.NoSignal };
        var board = NewBoard(host);

        await board.StopRecordingCommand.ExecuteAsync(null);

        Assert.False(board.HasPendingTake);
        Assert.NotNull(board.Notice);
    }

    // Save tests arrange the pending take directly: awaiting the async stop command would hop
    // the test off the thread that owns the Phrases CollectionView (a WPF test artifact — in
    // the app the await resumes on the dispatcher). The stop flow has its own tests above.
    [Fact]
    public void Save_take_saves_with_the_title_and_refreshes_the_board()
    {
        var host = new FakePlaybackHost();
        var board = NewBoard(host);
        board.PendingTake = Take();
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        Assert.Contains("SaveTake", host.Calls);
        Assert.Equal("Greeting", host.SavedTitle);
        Assert.False(board.HasPendingTake);
        Assert.Contains(board.Phrases, i => i.Title == "Greeting"); // appears on the board
    }

    [Fact]
    public void Save_take_is_disabled_when_the_title_is_blank()
    {
        var board = NewBoard(new FakePlaybackHost());
        board.PendingTake = Take();
        board.NewTitle = "   ";

        Assert.False(board.SaveTakeCommand.CanExecute(null));
    }

    [Fact]
    public void Save_take_becomes_enabled_once_a_title_is_typed()
    {
        var board = NewBoard(new FakePlaybackHost());
        board.PendingTake = Take();
        board.NewTitle = "";
        Assert.False(board.SaveTakeCommand.CanExecute(null));

        board.NewTitle = "Greeting";

        Assert.True(board.SaveTakeCommand.CanExecute(null));
    }

    [Fact]
    public void Save_take_failure_keeps_the_pending_take_and_shows_a_notice()
    {
        var host = new FakePlaybackHost { SaveTakeThrows = true };
        var board = NewBoard(host);
        board.PendingTake = Take();
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        Assert.True(board.HasPendingTake); // not lost — the operator can retry Save or Discard
        Assert.NotNull(board.Notice);
    }

    // The recorder save (WAV write + entry creation) already succeeded before the category/tag
    // step fails — retrying Save would call _recorder.SaveTake again and create a duplicate entry.
    // The take must be treated as saved; only a warning notice tells the operator to fix it manually.
    [Fact]
    public void Save_take_with_a_pending_category_that_fails_to_apply_still_completes_the_save()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live, // recording needs the engine running
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
            SetPhraseCategoryThrows = true,
        };
        var board = NewBoard(host);
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;
        board.RecordIntoCategoryCommand.Execute(null); // arranges _pendingMetadata.CategoryId synchronously (before its await)
        board.PendingTake = Take();
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        Assert.False(board.HasPendingTake); // the take IS considered saved, not lost
        Assert.Contains(board.Phrases, p => p.Title == "Greeting"); // it's on the board
        Assert.NotNull(board.Notice); // but the operator is warned
    }

    // IsProcessing is set synchronously before the first `await` inside StopRecording, so it is
    // already true the instant ExecuteAsync returns its (still-running) Task — this is not a race,
    // it's how C# async methods run their pre-await prefix on the caller's thread.
    [Fact]
    public async Task Stop_recording_reports_processing_until_the_take_is_ready()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);

        var stopTask = board.StopRecordingCommand.ExecuteAsync(null);
        Assert.True(board.IsProcessing);
        Assert.False(board.ShowRecordButton); // the idle Record button must not flash back

        await stopTask;

        Assert.False(board.IsProcessing);
        Assert.True(board.HasPendingTake);
    }

    [Fact]
    public async Task Stop_recording_clears_processing_even_on_failure()
    {
        var host = new FakePlaybackHost { NextStopResult = null }; // StopRecording throws below
        var board = NewBoard(host);
        host.ThrowOnStopRecording = true;

        await board.StopRecordingCommand.ExecuteAsync(null);

        Assert.False(board.IsProcessing);
    }

    [Fact]
    public void IsEmpty_reflects_whether_the_board_has_phrases()
    {
        Assert.True(NewBoard(new FakePlaybackHost()).IsEmpty);
        Assert.False(NewBoard(new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] }).IsEmpty);
    }

    [Fact]
    public void Saving_the_first_take_clears_the_empty_state_and_notifies()
    {
        var host = new FakePlaybackHost();
        var board = NewBoard(host);
        board.PendingTake = Take();
        Assert.True(board.IsEmpty);

        var changed = new List<string?>();
        board.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        board.SaveTakeCommand.Execute(null);

        Assert.False(board.IsEmpty);
        Assert.Contains(nameof(BoardViewModel.IsEmpty), changed);
    }

    [Fact]
    public void Saving_a_take_raises_the_Saved_event_with_the_title()
    {
        var host = new FakePlaybackHost();
        var board = NewBoard(host);
        board.PendingTake = Take();
        board.NewTitle = "Hello";

        string? saved = null;
        board.Saved += (_, title) => saved = title;

        board.SaveTakeCommand.Execute(null);

        Assert.Equal("Hello", saved);
    }

    [Fact]
    public async Task Discard_take_clears_it_without_saving()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);
        await board.StopRecordingCommand.ExecuteAsync(null);

        board.DiscardTakeCommand.Execute(null);

        Assert.False(board.HasPendingTake);
        Assert.DoesNotContain("SaveTake", host.Calls);
    }

    [Fact]
    public async Task Declining_the_discard_confirm_leaves_the_pending_take_untouched()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);
        await board.StopRecordingCommand.ExecuteAsync(null);
        board.SetConfirmDiscard(() => Task.FromResult(false));

        board.DiscardTakeCommand.Execute(null);

        Assert.True(board.HasPendingTake);
    }

    [Fact]
    public async Task Preview_take_plays_the_pending_samples_to_the_monitor()
    {
        var take = Take();
        var host = new FakePlaybackHost { NextStopResult = take };
        var board = NewBoard(host);
        await board.StopRecordingCommand.ExecuteAsync(null);

        await board.PreviewTakeCommand.ExecuteAsync(null);

        Assert.Contains("Preview", host.Calls);
        Assert.Same(take.Samples, host.PreviewedSamples);
    }

    // ---- Edit / delete -----------------------------------------------------------------------

    [Fact]
    public void Broken_phrases_are_flagged_from_the_host()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1" }, new PhraseEntry { Id = "p-2" }],
            BrokenPhraseIds = ["p-2"],
        };
        var board = NewBoard(host);

        Assert.False(board.Phrases[0].IsBroken);
        Assert.True(board.Phrases[1].IsBroken);
    }

    [Fact]
    public void Edit_command_updates_the_item_in_place_when_the_dialog_commits()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Old", CategoryId = Category.DefaultId }],
        };
        // The dialog edits the view-model, then "commits" (returns true).
        var board = NewBoard(host, showEditDialog: edit => { edit.Title = "New"; return true; });
        var item = board.Phrases[0];

        board.EditCommand.Execute(item);

        Assert.Equal("New", item.Title);              // same item, refreshed (the immutability trap)
        Assert.Equal("New", host.Phrases[0].Title);   // persisted through the seam
    }

    [Fact]
    public void Editing_a_phrase_out_of_the_active_filter_hides_it_from_the_view()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        var board = NewBoard(host, showEditDialog: edit =>
        {
            edit.SelectedCategoryId = Category.DefaultId; // move it out of "Greetings"
            return true;
        });
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;
        Assert.Single(VisibleTitles(board));

        board.EditCommand.Execute(board.Phrases[0]);

        Assert.Empty(VisibleTitles(board)); // moved out of the filtered view
        // The category is now genuinely empty (not just search-filtered) — CategoryIsEmpty owns
        // this case; SearchNoMatch is deliberately false here (it requires an active search).
        Assert.True(board.CategoryIsEmpty);
        Assert.False(board.SearchNoMatch);
    }

    [Fact]
    public void Edit_command_cancelled_changes_nothing()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Old", CategoryId = Category.DefaultId }],
        };
        var board = NewBoard(host, showEditDialog: edit => { edit.Title = "New"; return false; });
        var item = board.Phrases[0];

        board.EditCommand.Execute(item);

        Assert.Equal("Old", item.Title);
        Assert.Equal("Old", host.Phrases[0].Title);
    }

    [Fact]
    public void Closing_the_versions_window_still_reflects_a_version_deleted_eagerly_inside_it()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Greeting", Versions = [new PhraseVersion { Id = "pv-1", Label = "Take 2" }] }],
        };
        var board = NewBoard(host, showVersionsDialog: versions =>
        {
            versions.DeleteVersionCommand.Execute(versions.Tiles[1]); // eager — persists immediately (Tiles[0] is the primary)
        });

        board.ShowVersionsCommand.Execute(board.Phrases[0]);

        Assert.Empty(board.Phrases[0].Entry.Versions); // resynced from the library once the window closes
        Assert.Empty(host.Phrases[0].Versions);
    }

    [Fact]
    public void Add_version_starts_recording_for_that_phrase_without_closing_the_versions_window()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            CanRecord = true,
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Greeting" }],
        };
        // The real dialog's ShowDialog() blocks for the whole session (nested recorder included);
        // .GetAwaiter().GetResult() here mirrors that — a fire-and-forget Execute(null) would let this
        // fake return before the recording actually starts.
        var board = NewBoard(host, showVersionsDialog: versions =>
            versions.RecordVersionCommand.ExecuteAsync(null).GetAwaiter().GetResult());

        board.ShowVersionsCommand.Execute(board.Phrases[0]);

        Assert.True(board.IsRecording);
    }

    [Fact]
    public void Saving_a_recorded_version_updates_the_existing_tile_without_adding_a_new_one()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            CanRecord = true,
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Greeting" }],
        };
        var board = NewBoard(host, showVersionsDialog: versions =>
            versions.RecordVersionCommand.ExecuteAsync(null).GetAwaiter().GetResult());

        board.ShowVersionsCommand.Execute(board.Phrases[0]);
        Assert.True(board.IsRecording);

        board.PendingTake = Take();
        board.NewTitle = "Warm take";

        board.SaveTakeCommand.Execute(null);

        Assert.Equal("p-1", host.SavedVersionPhraseId);
        Assert.Equal("Warm take", host.SavedVersionLabel);
        Assert.Single(board.Phrases); // no new tile — the existing one is refreshed in place
        Assert.Single(host.Phrases[0].Versions);
        Assert.Null(board.PendingTake);
    }

    /// <summary>Regression test: saving a version must not end the session — the operator can keep
    /// clicking Record/Save inside the same still-open Recorder window to add several versions of the
    /// same phrase without reopening "Add version" each time.</summary>
    [Fact]
    public void Recording_a_second_take_in_the_same_session_also_saves_as_a_version()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            CanRecord = true,
            NextStopResult = Take(),
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Greeting" }],
        };
        var board = NewBoard(host, showVersionsDialog: versions =>
            versions.RecordVersionCommand.ExecuteAsync(null).GetAwaiter().GetResult());

#pragma warning disable xUnit1031
        board.ShowVersionsCommand.Execute(board.Phrases[0]);
        board.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        board.NewTitle = "Take A";
        board.SaveTakeCommand.Execute(null);

        // Still the same session (the Recorder window was never closed) — Record again.
        board.StartRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        board.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        board.NewTitle = "Take B";
        board.SaveTakeCommand.Execute(null);

        Assert.Equal(2, host.Phrases[0].Versions.Count);
        Assert.Equal("Take A", host.Phrases[0].Versions[0].Label);
        Assert.Equal("Take B", host.Phrases[0].Versions[1].Label);
        Assert.Single(board.Phrases); // still no new tile — both takes filed under the one phrase
    }

    /// <summary>Regression test: once the Recorder window actually closes, the version stash must be
    /// gone — otherwise the operator's next, unrelated recording would silently be filed as yet
    /// another version of the old phrase instead of becoming its own new phrase.</summary>
    [Fact]
    public void Ending_the_version_session_lets_the_next_recording_create_a_new_phrase()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            CanRecord = true,
            NextStopResult = Take(),
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Greeting" }],
        };
        var board = NewBoard(host, showVersionsDialog: versions =>
            versions.RecordVersionCommand.ExecuteAsync(null).GetAwaiter().GetResult());

#pragma warning disable xUnit1031
        board.ShowVersionsCommand.Execute(board.Phrases[0]);
        board.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        board.NewTitle = "Take A";
        board.SaveTakeCommand.Execute(null);

        board.EndVersionRecordingSession(); // simulates RecorderDialog.OnClosing

        board.StartRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        board.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        board.NewTitle = "Unrelated";
        board.SaveTakeCommand.Execute(null);

        Assert.Contains(host.Phrases, p => p.Title == "Unrelated"); // a new phrase, not another version
        Assert.Single(host.Phrases[0].Versions); // the earlier phrase only ever got the one version
    }

    /// <summary>Regression test for the leak this design explicitly guards against: a stash left over
    /// from a failed "record a version" attempt must never misfile the operator's next, unrelated
    /// recording as a version of some old phrase.</summary>
    [Fact]
    public void A_failed_version_recording_does_not_leak_into_the_next_unrelated_recording()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            CanRecord = false, // the version-record attempt fails
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Greeting" }],
        };
        var board = NewBoard(host, showVersionsDialog: versions =>
            versions.RecordVersionCommand.ExecuteAsync(null).GetAwaiter().GetResult());

        board.ShowVersionsCommand.Execute(board.Phrases[0]);
        Assert.False(board.IsRecording); // the version attempt failed, and cleared its own stash

        host.CanRecord = true;
#pragma warning disable xUnit1031
        board.StartRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        board.PendingTake = Take();
        board.NewTitle = "Unrelated";

        board.SaveTakeCommand.Execute(null);

        Assert.Contains(host.Phrases, p => p.Title == "Unrelated"); // a new phrase, not a version
        Assert.Null(host.SavedVersionPhraseId); // SaveTakeAsVersion was never called
    }

    /// <summary>Review finding 7: if the phrase a version was stashed for is gone by the time Save
    /// runs (e.g. deleted from another view while the Recorder stayed open), the host reports no
    /// update — Save must show that, not claim "New version saved" and discard the take.</summary>
    [Fact]
    public void Saving_a_version_for_a_phrase_that_no_longer_exists_shows_an_error_and_keeps_the_pending_take()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            CanRecord = true,
            NextStopResult = Take(),
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Greeting" }],
        };
        var board = NewBoard(host, showVersionsDialog: versions =>
            versions.RecordVersionCommand.ExecuteAsync(null).GetAwaiter().GetResult());
        BoardNotification? seen = null;
        board.Notified += (_, n) => seen = n;

#pragma warning disable xUnit1031
        board.ShowVersionsCommand.Execute(board.Phrases[0]);
        board.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        host.Phrases = []; // the phrase was deleted elsewhere while the Recorder stayed open
        board.NewTitle = "Take A";

        board.SaveTakeCommand.Execute(null);

        Assert.NotNull(seen);
        Assert.Equal(NoticeSeverity.Error, seen.Severity);
        Assert.NotNull(board.PendingTake); // the take was kept, not silently discarded
    }

    [Fact]
    public async Task Delete_command_orphans_removes_and_raises_Deleted_when_confirmed()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1", Title = "Bye" }] };
        var board = NewBoard(host, confirmDelete: _ => Task.FromResult(true));
        var item = board.Phrases[0];
        string? deleted = null;
        board.Deleted += (_, title) => deleted = title;

        await board.DeleteCommand.ExecuteAsync(item);

        Assert.Empty(board.Phrases);
        Assert.Equal("p-1", Assert.Single(host.Deleted).Id);
        Assert.Equal("Bye", deleted);
    }

    [Fact]
    public async Task Delete_command_does_nothing_when_not_confirmed()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] };
        var board = NewBoard(host, confirmDelete: _ => Task.FromResult(false));

        await board.DeleteCommand.ExecuteAsync(board.Phrases[0]);

        Assert.Single(board.Phrases);
        Assert.Empty(host.Deleted);
    }

    // ---- Category colour ---------------------------------------------------------------------

    [Fact]
    public void Phrase_items_carry_their_category_colour()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings", Color = "#54D262" }],
            Phrases = [new PhraseEntry { Id = "p-1", CategoryId = "c-1" }],
        };

        var board = NewBoard(host);

        Assert.Equal("#54D262", board.Phrases[0].CategoryColor);
    }

    [Fact]
    public void Editing_a_phrase_into_another_category_updates_its_colour()
    {
        var host = new FakePlaybackHost
        {
            Categories =
            [
                new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" },
                new Category { Id = "c-1", Name = "Greetings", Color = "#54D262" },
            ],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = Category.DefaultId }],
        };
        var board = NewBoard(host, showEditDialog: edit => { edit.SelectedCategoryId = "c-1"; return true; });

        board.EditCommand.Execute(board.Phrases[0]);

        Assert.Equal("#54D262", board.Phrases[0].CategoryColor);
    }

    [Fact]
    public void A_saved_take_gets_the_default_category_colour()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" }],
        };
        var board = NewBoard(host);
        board.PendingTake = Take();

        board.SaveTakeCommand.Execute(null);

        Assert.Equal("#808080", board.Phrases[0].CategoryColor);
    }

    [Fact]
    public void Recolouring_a_category_in_the_manager_re_tints_its_tiles()
    {
        var host = new FakePlaybackHost
        {
            Categories =
            [
                new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" },
                new Category { Id = "c-1", Name = "Greetings", Color = "#54D262" },
            ],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        // The "manager" recolours Greetings and saves, like the user would.
        var board = NewBoard(host, showManageCategories: library =>
        {
            var vm = new CategoriesViewModel(library);
            var row = vm.Rows.First(r => r.Id == "c-1");
            row.Color = "#FF6B6B"; // the dropdown sets the row's colour
            vm.SaveCommand.Execute(row);
        });

        board.ManageCategoriesCommand.Execute(null);

        Assert.Equal("#FF6B6B", board.Phrases[0].CategoryColor);
    }

    // ---- Tag chips -----------------------------------------------------------------------------

    [Fact]
    public void Phrase_items_carry_their_tags_as_coloured_chips()
    {
        var host = new FakePlaybackHost
        {
            Tags = [new TagInfo { Name = "opening", Color = "#4CC2FF" }, new TagInfo { Name = "urgent", Color = "#FF6B6B" }],
            Phrases = [new PhraseEntry { Id = "p-1", Tags = ["opening", "urgent"] }],
        };

        var board = NewBoard(host);

        Assert.Equal(
            [new TagChipViewModel("opening", "#4CC2FF"), new TagChipViewModel("urgent", "#FF6B6B")],
            board.Phrases[0].TagChips);
    }

    [Fact]
    public void Tag_chip_colour_resolves_case_insensitively()
    {
        // The phrase stores "Opening" (its own casing); the registry has "opening" (first-used casing).
        var host = new FakePlaybackHost
        {
            Tags = [new TagInfo { Name = "opening", Color = "#4CC2FF" }],
            Phrases = [new PhraseEntry { Id = "p-1", Tags = ["Opening"] }],
        };

        var board = NewBoard(host);

        Assert.Equal("#4CC2FF", board.Phrases[0].TagChips.Single().Color);
    }

    [Fact]
    public void An_unregistered_tag_gets_an_empty_chip_colour()
    {
        // A tag on the phrase that never went through SetPhraseTags (so it has no registry entry).
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Tags = ["mystery"] }],
        };

        var board = NewBoard(host);

        Assert.Equal("", board.Phrases[0].TagChips.Single().Color);
    }

    [Fact]
    public void Editing_a_phrases_tags_refreshes_its_chips()
    {
        var host = new FakePlaybackHost
        {
            Tags = [new TagInfo { Name = "opening", Color = "#4CC2FF" }],
            Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = Category.DefaultId, Tags = [] }],
        };
        var board = NewBoard(host, showEditDialog: edit => { edit.Tags.Add("opening"); return true; });

        board.EditCommand.Execute(board.Phrases[0]);

        Assert.Equal("opening", board.Phrases[0].TagChips.Single().Name);
        Assert.Equal("#4CC2FF", board.Phrases[0].TagChips.Single().Color);
    }

    // ---- Search / filter ---------------------------------------------------------------------

    private static IEnumerable<string> VisibleTitles(BoardViewModel board) =>
        board.PhrasesView.Cast<PhraseItemViewModel>().Select(i => i.Title);

    [Fact]
    public void Search_filters_by_title_case_insensitively()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hello" }, new PhraseEntry { Id = "p-2", Title = "Goodbye" }],
        };
        var board = NewBoard(host);

        board.SearchText = "hell";

        Assert.Equal(["Hello"], VisibleTitles(board));
        Assert.False(board.SearchNoMatch);
    }

    [Fact]
    public void Search_also_matches_tags()
    {
        var host = new FakePlaybackHost
        {
            Phrases =
            [
                new PhraseEntry { Id = "p-1", Title = "A", Tags = ["greeting"] },
                new PhraseEntry { Id = "p-2", Title = "B", Tags = ["closing"] },
            ],
        };
        var board = NewBoard(host);

        board.SearchText = "greet";

        Assert.Equal(["A"], VisibleTitles(board));
    }

    [Fact]
    public void Category_filter_limits_to_the_chosen_category()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings" }],
            Phrases =
            [
                new PhraseEntry { Id = "p-1", Title = "A", CategoryId = "c-1" },
                new PhraseEntry { Id = "p-2", Title = "B", CategoryId = Category.DefaultId },
            ],
        };
        var board = NewBoard(host);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;

        Assert.Equal(["A"], VisibleTitles(board));
    }

    [Fact]
    public void Category_is_empty_when_selected_category_has_no_phrases_and_search_is_blank()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }, new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        var board = NewBoard(host);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;

        Assert.True(board.CategoryIsEmpty);
    }

    [Fact]
    public void Category_is_not_empty_when_it_has_a_phrase()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        var board = NewBoard(host);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;

        Assert.False(board.CategoryIsEmpty);
    }

    [Fact]
    public void Category_is_not_reported_empty_while_search_text_is_active()
    {
        // Search-no-match (Task 3) owns this case instead — the two states are mutually exclusive
        // by construction (one requires blank search text, the other requires non-blank).
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        var board = NewBoard(host);
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;

        board.SearchText = "hello";

        Assert.False(board.CategoryIsEmpty);
    }

    [Fact]
    public void Category_is_not_empty_when_all_categories_is_selected()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi" }] };
        var board = NewBoard(host);

        Assert.False(board.CategoryIsEmpty); // default: nothing checked, shows every phrase
    }

    [Fact]
    public void Checking_two_categories_shows_phrases_from_either()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }, new Category { Id = "c-2", Name = "Closers" }],
            Phrases =
            [
                new PhraseEntry { Id = "p-1", Title = "A", CategoryId = "c-1" },
                new PhraseEntry { Id = "p-2", Title = "B", CategoryId = "c-2" },
                new PhraseEntry { Id = "p-3", Title = "C", CategoryId = Category.DefaultId },
            ],
        };
        var board = NewBoard(host);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;

        Assert.Equal(["A", "B"], VisibleTitles(board));
    }

    [Fact]
    public void Unchecking_the_last_category_shows_every_phrase_again()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }],
            Phrases =
            [
                new PhraseEntry { Id = "p-1", Title = "A", CategoryId = "c-1" },
                new PhraseEntry { Id = "p-2", Title = "B", CategoryId = Category.DefaultId },
            ],
        };
        var board = NewBoard(host);
        var item = board.CategoryFilterItems.Single(i => i.Category.Id == "c-1");
        item.IsChecked = true;
        Assert.Equal(["A"], VisibleTitles(board));

        item.IsChecked = false;

        Assert.Equal(["A", "B"], VisibleTitles(board));
    }

    [Fact]
    public void Two_categories_checked_with_no_matches_shows_the_generic_empty_state_not_the_cta()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }, new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = Category.DefaultId }],
        };
        var board = NewBoard(host);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;

        Assert.False(board.CategoryIsEmpty); // 2+ checked — no single target to record into
        Assert.True(board.MultipleCategoriesNoMatch);
    }

    [Fact]
    public void Category_filter_button_label_summarizes_the_checked_set()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }, new Category { Id = "c-2", Name = "Closers" }],
        };
        var board = NewBoard(host);
        Assert.Equal("Categories", board.CategoryFilterButtonLabel);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;
        Assert.Equal("Openers", board.CategoryFilterButtonLabel);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;
        Assert.Equal("2 categories", board.CategoryFilterButtonLabel);
    }

    [Fact]
    public void Conversation_filter_button_label_reflects_the_active_conversation()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1" }],
            Conversations = [new Conversation { Id = "v-1", Name = "Cold call", PhraseIds = ["p-1"] }],
        };
        var board = NewBoard(host);
        Assert.Equal("Conversations", board.ConversationFilterButtonLabel);

        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        Assert.Equal("Cold call", board.ConversationFilterButtonLabel);
    }

    [Fact]
    public async Task Record_into_category_starts_recording_like_the_normal_Record_button()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live, // recording needs the engine running
            CanRecord = true,
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
        };
        var board = NewBoard(host);
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;

        await board.RecordIntoCategoryCommand.ExecuteAsync(null);

        Assert.True(board.IsRecording);
    }

    [Fact]
    public void Record_into_category_applies_the_category_to_the_saved_take()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live, // recording needs the engine running
            CanRecord = true,
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
        };
        var board = NewBoard(host);
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;

        // Execute (not ExecuteAsync + await) — RecordIntoCategory stashes the pending category
        // synchronously before its first await, and awaiting the full command here would hop this
        // test off the thread that owns the Phrases CollectionView (the same WPF test artifact the
        // Save tests above avoid).
        board.RecordIntoCategoryCommand.Execute(null);
        board.PendingTake = Take();
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Greeting");
        Assert.Equal("c-2", saved.CategoryId);
    }

    [Fact]
    public void Discarding_a_take_clears_any_pending_category()
    {
        var host = new FakePlaybackHost { CanRecord = true, Categories = [new Category { Id = "c-2", Name = "Closers" }] };
        var board = NewBoard(host);
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;
        board.RecordIntoCategoryCommand.Execute(null); // synchronous — see comment above
        board.DiscardTakeCommand.Execute(null);

        // A later, unrelated save must NOT pick up the stale pending category.
        board.PendingTake = Take();
        board.NewTitle = "Unrelated";
        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Unrelated");
        Assert.Equal(Category.DefaultId, saved.CategoryId);
    }

    // A recording attempt that never actually starts (host says not live) never creates a
    // PendingTake, so SaveTake/DiscardTake — the only places that normally clear _pendingMetadata —
    // never run. The stash must still be cleared by the failed StartRecording itself, or it would
    // silently misfile the operator's next, unrelated recording.
    [Fact]
    public void Failed_record_into_category_does_not_leak_pending_metadata_into_the_next_save()
    {
        var host = new FakePlaybackHost { CanRecord = false, Categories = [new Category { Id = "c-2", Name = "Closers" }] };
        var board = NewBoard(host);
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;

        // Block instead of `await` — see the comment on the repair-dialog re-record test above for
        // why: this keeps the assertions below on the thread that owns the Phrases CollectionView.
#pragma warning disable xUnit1031
        board.RecordIntoCategoryCommand.ExecuteAsync(null).GetAwaiter().GetResult();
#pragma warning restore xUnit1031

        Assert.False(board.IsRecording);
        Assert.NotNull(board.Notice); // "Press Start to go Live before recording."

        // A later, unrelated save must NOT pick up the stale pending category from the failed attempt.
        board.PendingTake = Take();
        board.NewTitle = "Unrelated";
        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Unrelated");
        Assert.Equal(Category.DefaultId, saved.CategoryId);
    }

    // A re-record that starts fine but then stops with no signal also never creates a PendingTake,
    // so — same as the failed-start case above — the stash must be cleared by StopRecording itself,
    // or it would misfile (title, category, and tags) the operator's next, unrelated take.
    [Fact]
    public void Re_record_that_stops_with_no_signal_does_not_leak_the_title_into_the_next_save()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live, // recording needs the engine running
            CanRecord = true,
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-2", Tags = ["urgent"] }],
            BrokenPhraseIds = ["p-1"],
            NextStopResult = RecordingResult.NoSignal,
        };
        var board = NewBoard(host, showRepairDialog: repair => { repair.ChooseReRecord(); return true; });

#pragma warning disable xUnit1031
        board.PlayCommand.ExecuteAsync(board.Phrases[0]).GetAwaiter().GetResult();
        board.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.False(board.HasPendingTake); // no signal — nothing to save from this attempt

        // A later, unrelated record + save must not pick up the stale title/category/tags.
        host.NextStopResult = Take();
        board.StartRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        board.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
#pragma warning restore xUnit1031

        Assert.NotEqual("Hi", board.NewTitle); // fresh timestamp default, not the leaked title
        board.NewTitle = "Unrelated";
        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Unrelated");
        Assert.Equal(Category.DefaultId, saved.CategoryId);
        Assert.Empty(saved.Tags);
    }

    [Fact]
    public void No_matches_state_is_distinct_from_first_run_empty()
    {
        var board = NewBoard(new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1", Title = "Hello" }] });
        board.SearchText = "zzz";

        Assert.True(board.SearchNoMatch);  // phrases exist, none match
        Assert.False(board.IsEmpty);   // not the first-run welcome
        Assert.False(board.HasMatches);

        var firstRun = NewBoard(new FakePlaybackHost());
        Assert.True(firstRun.IsEmpty);     // first-run welcome
        Assert.False(firstRun.SearchNoMatch);  // not a "no matches" result
    }

    [Fact]
    public void Search_no_match_is_true_even_with_a_category_selected()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hello", CategoryId = "c-1" }],
        };
        var board = NewBoard(host);
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;

        board.SearchText = "zzz";

        Assert.True(board.SearchNoMatch);
        Assert.False(board.CategoryIsEmpty); // the category itself isn't empty — the search is
    }

    [Fact]
    public void Has_search_text_reflects_whether_a_query_is_active()
    {
        var board = NewBoard(new FakePlaybackHost());
        Assert.False(board.HasSearchText);

        board.SearchText = "x";

        Assert.True(board.HasSearchText);
    }

    [Fact]
    public void Clear_search_resets_the_search_text()
    {
        var board = NewBoard(new FakePlaybackHost());
        board.SearchText = "xyz";

        board.ClearSearchCommand.Execute(null);

        Assert.Equal("", board.SearchText);
        Assert.False(board.HasSearchText);
    }

    [Fact]
    public void Manage_categories_opens_the_manager_then_rebuilds_the_filter_options()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized" }],
        };
        // The "dialog" adds a category, like the user would.
        var board = NewBoard(host, showManageCategories: library =>
        {
            var vm = new CategoriesViewModel(library);
            vm.NewName = "Greetings";
            vm.AddCommand.Execute(null);
        });

        board.ManageCategoriesCommand.Execute(null);

        Assert.Contains(board.CategoryFilterItems, i => i.Category.Name == "Greetings");
        Assert.All(board.CategoryFilterItems, i => Assert.False(i.IsChecked)); // every row starts unchecked
    }

    [Fact]
    public void Run_setup_opens_the_wizard_with_the_current_hotkey_status()
    {
        var host = new FakePlaybackHost();
        SetupWizardViewModel? shown = null;
        var board = NewBoard(host, showSetupWizard: vm => shown = vm);

        board.RunSetupCommand.Execute(null);

        var hotkeyStep = Assert.IsType<HotkeyStatusStepViewModel>(shown!.Steps[2]);
        Assert.Equal("Global stop hotkey registered: Pause", hotkeyStep.StatusLabel);
    }

    /// <summary>RunSettings no longer builds the SettingsWindowViewModel itself (Pass 2b) — the
    /// window does, once it exists, since its dialog-prompt delegates are the window's own async
    /// methods. This asserts the raw ingredients BoardViewModel hands over are correct.</summary>
    [Fact]
    public void Run_settings_hands_the_hosts_hotkey_and_export_picker_to_the_window()
    {
        var host = new FakePlaybackHost();
        var settingsHost = new FakeSettingsHost();
        ISettingsHost? shownSettingsHost = null;
        ISetupHost? shownSetup = null;
        string? shownHotkey = null;
        Func<string?>? shownPickExportPath = null;
        var board = NewBoard(host, settingsHost: settingsHost, showSettings: (s, setup, hotkey, pick) =>
        {
            shownSettingsHost = s;
            shownSetup = setup;
            shownHotkey = hotkey;
            shownPickExportPath = pick;
        });

        board.RunSettingsCommand.Execute(null);

        Assert.Same(settingsHost, shownSettingsHost);
        Assert.Same(host, shownSetup);
        Assert.Equal("Pause", shownHotkey);
        Assert.NotNull(shownPickExportPath);
    }

    // ---- Conversations -------------------------------------------------------------------------

    [Fact]
    public void Selecting_a_conversation_shows_only_its_phrases_in_step_order()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [
                new PhraseEntry { Id = "p-1", Title = "A" },
                new PhraseEntry { Id = "p-2", Title = "B" },
                new PhraseEntry { Id = "p-3", Title = "C" }, // not in the conversation
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-2", "p-1"] }],
        };
        var board = NewBoard(host);

        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        var visible = board.PhrasesView.Cast<PhraseItemViewModel>().Select(p => p.Entry.Id).ToList();
        Assert.Equal(["p-2", "p-1"], visible); // filtered to the conversation, in its order
    }

    [Fact]
    public void Selecting_a_conversation_turns_off_the_category_filter()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings" }],
            Phrases = [new PhraseEntry { Id = "p-1", CategoryId = "c-1" }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"] }],
        };
        var board = NewBoard(host);
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;

        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        Assert.All(board.CategoryFilterItems, i => Assert.False(i.IsChecked)); // cleared
        Assert.False(board.CategoryFilterEnabled);
    }

    [Fact]
    public void Selecting_a_specific_category_turns_off_an_active_conversation()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings" }],
            Phrases = [new PhraseEntry { Id = "p-1", CategoryId = "c-1" }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"] }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;

        Assert.False(board.IsConversationActive);
        Assert.Equal(BoardViewModel.NoneConversation.Id, board.SelectedConversationFilter.Id);
    }

    [Fact]
    public void Playing_a_phrase_in_the_active_conversation_highlights_the_next_step()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav" },
                new PhraseEntry { Id = "p-2", FileName = "p-2.wav" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1", "p-2"] }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");
        Assert.True(board.Phrases.Single(p => p.Entry.Id == "p-1").IsCurrentStep); // starts at step 0

        board.PlayCommand.Execute(board.Phrases.Single(p => p.Entry.Id == "p-1"));

        Assert.False(board.Phrases.Single(p => p.Entry.Id == "p-1").IsCurrentStep);
        Assert.True(board.Phrases.Single(p => p.Entry.Id == "p-2").IsCurrentStep);
    }

    [Fact]
    public void Playing_an_out_of_order_phrase_jumps_the_pointer_to_just_after_it()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav" },
                new PhraseEntry { Id = "p-2", FileName = "p-2.wav" },
                new PhraseEntry { Id = "p-3", FileName = "p-3.wav" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1", "p-2", "p-3"] }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.PlayCommand.Execute(board.Phrases.Single(p => p.Entry.Id == "p-3")); // caller jumped ahead

        Assert.DoesNotContain(board.Phrases, p => p.IsCurrentStep); // past the last step — nothing highlighted
    }

    [Fact]
    public void Reselecting_a_conversation_resets_the_step_pointer_to_the_first_phrase()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav" },
                new PhraseEntry { Id = "p-2", FileName = "p-2.wav" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1", "p-2"] }],
        };
        var board = NewBoard(host);
        var conversation = board.ConversationFilterOptions.Single(c => c.Id == "v-1");
        board.SelectedConversationFilter = conversation;
        board.PlayCommand.Execute(board.Phrases.Single(p => p.Entry.Id == "p-1")); // pointer now at p-2

        board.SelectedConversationFilter = BoardViewModel.NoneConversation;
        board.SelectedConversationFilter = conversation; // re-select the same conversation

        Assert.True(board.Phrases.Single(p => p.Entry.Id == "p-1").IsCurrentStep); // back to step 0
    }

    [Fact]
    public void Activating_the_already_active_conversation_restarts_its_step_pointer()
    {
        // Conversation is a record (value equality) and SelectedConversationFilter is an
        // ObservableProperty, so a plain re-assignment to an equal value is a silent no-op — the
        // menu's "click a conversation row" path must go through ActivateConversation instead of
        // setting SelectedConversationFilter directly, or clicking the already-active row would do
        // nothing (no step-pointer reset).
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav" },
                new PhraseEntry { Id = "p-2", FileName = "p-2.wav" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1", "p-2"] }],
        };
        var board = NewBoard(host);
        var conversation = board.ConversationFilterOptions.Single(c => c.Id == "v-1");
        board.ActivateConversation(conversation);
        board.PlayCommand.Execute(board.Phrases.Single(p => p.Entry.Id == "p-1")); // pointer now at p-2

        board.ActivateConversation(conversation); // re-activate the same conversation

        Assert.True(board.Phrases.Single(p => p.Entry.Id == "p-1").IsCurrentStep); // back to step 0
    }

    [Fact]
    public void Activating_none_while_already_none_is_a_harmless_no_op()
    {
        var board = NewBoard(new FakePlaybackHost());

        board.ActivateConversation(BoardViewModel.NoneConversation);

        Assert.False(board.IsConversationActive);
    }

    [Fact]
    public void Switching_to_none_exits_conversation_mode_and_shows_every_phrase()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [
                new PhraseEntry { Id = "p-1" },
                new PhraseEntry { Id = "p-2" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"] }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.SelectedConversationFilter = BoardViewModel.NoneConversation;

        Assert.Equal(2, board.PhrasesView.Cast<PhraseItemViewModel>().Count());
        Assert.True(board.CategoryFilterEnabled);
    }

    [Fact]
    public void Conversation_with_no_matching_phrases_shows_the_empty_state()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "A" }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = [] }],
        };
        var board = NewBoard(host);

        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        Assert.True(board.ConversationIsEmpty);
        Assert.False(board.IsEmpty); // the board itself has phrases
        Assert.False(board.CategoryIsEmpty); // mutually exclusive by construction
    }

    // Regression test for the NRE a self-review caught in OnSelectedConversationFilterChanged
    // (commit e8bdcba): a live WPF binding on ConversationIsEmpty reads the property synchronously
    // from inside the PropertyChanged handler. A plain unit test with no subscriber can never see
    // this — OnPropertyChanged just raises an event; nothing evaluates the getter unless something
    // is listening, the way a real binding does. This test simulates that listener.
    //
    // Selecting a category first means a specific category is checked when the conversation gets
    // selected, so OnSelectedConversationFilterChanged has to clear that checked category — clearing
    // it re-enters OnCategoryFilterItemChanged -> RefreshFilter -> OnPropertyChanged(ConversationIsEmpty),
    // while IsConversationActive already reports true. If _activeConversationPhraseIdSet were still
    // null at that point (the pre-fix ordering), the handler's read of ConversationIsEmpty would NRE
    // inside `_activeConversationPhraseIdSet!.Contains(...)`.
    [Fact]
    public void Selecting_a_conversation_while_a_category_is_active_does_not_null_ref_a_live_binding()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"] }],
        };
        var board = NewBoard(host);
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;

        var conversationIsEmptyNotified = false;
        var observedConversationIsEmpty = false;
        board.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BoardViewModel.ConversationIsEmpty))
            {
                conversationIsEmptyNotified = true;
                // A real WPF binding reads the getter synchronously right here — this is the read
                // that NREs pre-fix, when _activeConversationPhraseIdSet is still null.
                observedConversationIsEmpty = board.ConversationIsEmpty;
            }
        };

        var exception = Record.Exception(() =>
            board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1"));

        Assert.Null(exception);
        // If this ever stops being true, the re-entrant notification chain silently stopped firing
        // for ConversationIsEmpty — the test would otherwise pass for the wrong reason.
        Assert.True(conversationIsEmptyNotified);
        Assert.False(observedConversationIsEmpty); // the conversation's one phrase is visible
    }

    [Fact]
    public void ManageConversations_shows_the_dialog_and_refreshes_the_filter_options()
    {
        var host = new FakePlaybackHost();
        ILibraryHost? shown = null;
        var board = NewBoard(host, showManageConversations: library => shown = library);

        host.Conversations = [new Conversation { Id = "v-new", Name = "Added mid-dialog" }];
        board.ManageConversationsCommand.Execute(null);

        Assert.Same(host, shown);
        Assert.Contains(board.ConversationFilterOptions, c => c.Id == "v-new");
        Assert.Equal(BoardViewModel.NoneConversation.Id, board.SelectedConversationFilter.Id);
    }
}
