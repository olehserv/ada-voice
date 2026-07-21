using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Audio.Setup;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// Renders every WPF window (and each setup-wizard step) with a fake-backed view-model and saves a
/// PNG under <c>docs/ui/screenshots/after</c> for visual inspection. These drive real windows on
/// screen, so they need an interactive desktop session — exclude them from headless CI with
/// <c>dotnet test --filter "Category!=Screenshot"</c>.
/// </summary>
[Collection(WpfAppCollection.Name)]
[Trait("Category", "Screenshot")]
public sealed class WindowScreenshotTests(WpfAppFixture app)
{
    private readonly ScreenshotHarness _harness = new(app);

    // ---- windows ----

    [ScreenshotFact]
    public void MainWindow_board() =>
        Save(() => new MainWindow { DataContext = NewBoard() }, "main-board");

    /// <summary>Structural-fix-plan Pass 3 (audit C1): the default 420x640 screenshot never proves
    /// the search/filter row and phrase WrapPanel hold up at a typical desktop width. Same board
    /// view-model, just a wider window and more phrases so the WrapPanel actually re-flows.</summary>
    [ScreenshotFact]
    public void MainWindow_board_wide()
    {
        Save(() =>
        {
            var window = new MainWindow { DataContext = NewWideBoard(), Width = 1366, Height = 780 };
            return window;
        }, "main-board-wide");
    }

    /// <summary>Slice 3 (responsive layout, resolved 2026-07-20): the wide screenshot above proves
    /// the layout holds at a typical desktop width, but the enforced <c>MinWidth="420"</c> — the
    /// primary real-world Docked shape — was never rendered wider than the default 480 px fixture.
    /// Same stress-loaded board as <see cref="MainWindow_board_wide"/>, pinned at the minimum.</summary>
    [ScreenshotFact]
    public void MainWindow_board_docked()
    {
        Save(() =>
        {
            var window = new MainWindow { DataContext = NewWideBoard(), Width = 420, Height = 560 };
            return window;
        }, "main-board-docked");
    }

    /// <summary>
    /// Phase B (brand redesign): the state-lit window backdrop, status pill, and STOP fill all vary
    /// by <see cref="EngineState"/> — the owner reviews the signature by seeing all four, not just
    /// the one LIVE state <see cref="MainWindow_board"/> already covers.
    /// </summary>
    [ScreenshotFact]
    public void MainWindow_board_stopped() =>
        Save(() => new MainWindow { DataContext = NewBoard(EngineState.Stopped) }, "main-board-stopped");

    [ScreenshotFact]
    public void MainWindow_board_offAir() =>
        Save(() => new MainWindow { DataContext = NewBoard(EngineState.OffAir) }, "main-board-offair");

    [ScreenshotFact]
    public void MainWindow_board_degraded() =>
        Save(() => new MainWindow { DataContext = NewBoard(EngineState.Degraded) }, "main-board-degraded");

    /// <summary>Phase B item 3's Accent-border + Live-tint tile fill only shows on a playing tile —
    /// <see cref="MainWindow_board"/>'s fixture never plays anything, so this is the only screenshot
    /// that renders it.</summary>
    [ScreenshotFact]
    public void MainWindow_board_playing()
    {
        var host = SampleHost();
        var board = NewBoardFor(host);
        host.RaisePlayingPhraseChanged("p-1");
        Save(() => new MainWindow { DataContext = board }, "main-board-playing");
    }

    /// <summary>Phase B's tile rework made the broken-audio warning replace the tag strip
    /// (mutually exclusive via a DataTrigger) — unverified by any other fixture, none of which
    /// mark a phrase broken.</summary>
    [ScreenshotFact]
    public void MainWindow_board_broken()
    {
        var host = SampleHost();
        host.BrokenPhraseIds = ["p-1"];
        Save(() => new MainWindow { DataContext = NewBoardFor(host) }, "main-board-broken");
    }

    /// <summary>The empty-state "Record" CTA (Phase B: direct Background, not Appearance="Primary")
    /// only renders when the board has no phrases.</summary>
    [ScreenshotFact]
    public void MainWindow_board_empty()
    {
        var host = SampleHost();
        host.Phrases = [];
        Save(() => new MainWindow { DataContext = NewBoardFor(host) }, "main-board-empty");
    }

    [ScreenshotFact]
    public void SettingsWindow() =>
        Save(() => new SettingsWindow { DataContext = NewSettings() }, "settings");

    [ScreenshotFact]
    public void SetupWizardWindow() =>
        Save(() => new SetupWizardWindow { DataContext = new SetupWizardViewModel(SampleHost(), "Pause") },
            "setup-wizard");

    /// <summary>The default fixture's Environment-checks step never finishes its async check run in
    /// time for the screenshot, so Next stays disabled — no fixture ever rendered Next in its real
    /// enabled state (Phase C's Appearance="Primary" -> BrandCtaButtonStyle fix on this button).
    /// InstructionStepViewModel.CanAdvance is unconditionally true, so jumping straight to it is the
    /// simplest way to exercise an enabled Next.</summary>
    [ScreenshotFact]
    public void SetupWizardWindow_nextEnabled() =>
        Save(() =>
        {
            var vm = new SetupWizardViewModel(SampleHost(), "Pause") { CurrentStepIndex = 3 };
            return new SetupWizardWindow { DataContext = vm };
        }, "setup-wizard-next-enabled");

    [ScreenshotFact]
    public void RecorderDialog() =>
        Save(() => new RecorderDialog { DataContext = NewBoard() }, "recorder");

    /// <summary>Phase C Step 6: the elapsed-time text next to "Recording… speak now" — no fixture
    /// ever rendered the recording state before this. The exact elapsed value is nondeterministic
    /// (a few tenths of a second by the time the harness captures) — expected, this is a visual
    /// sanity check, not a pixel-exact baseline.</summary>
    [ScreenshotFact]
    public void RecorderDialog_recording()
    {
        var board = NewBoard();
        board.IsRecording = true;
        Save(() => new RecorderDialog { DataContext = board }, "recorder-recording");
    }

    /// <summary>Phase C Step 6: the pending-take save form with the reordered Discard/Preview/Save
    /// row (Discard now first and Danger-red) — no fixture ever rendered this state before.</summary>
    [ScreenshotFact]
    public void RecorderDialog_pendingTake()
    {
        var board = NewBoard();
        board.PendingTake = new RecordingResult(new float[10], GainDb: -3, DurationMs: 2400, PeakDbfs: -6);
        Save(() => new RecorderDialog { DataContext = board }, "recorder-pending-take");
    }

    [ScreenshotFact]
    public void ManageCategoriesDialog() =>
        Save(() => new ManageCategoriesDialog { DataContext = new CategoriesViewModel(SampleHost()) },
            "manage-categories");

    [ScreenshotFact]
    public void ManageConversationsDialog() =>
        Save(() => new ManageConversationsDialog { DataContext = new ConversationsViewModel(SampleHost()) },
            "manage-conversations");

    [ScreenshotFact]
    public void PhraseEditDialog()
    {
        var host = SampleHost();
        Save(() => new PhraseEditDialog { DataContext = new PhraseEditViewModel(host, host.Phrases[0]) },
            "phrase-edit");
    }

    [ScreenshotFact]
    public void PhraseVersionsDialog()
    {
        var host = SampleHost();
        Save(() => new PhraseVersionsDialog { DataContext = new PhraseVersionsViewModel(host, host, host.Phrases[0]) },
            "phrase-versions");
    }

    /// <summary>Security scan 2026-07-12 finding 5: a version whose WAV is missing shows an
    /// "audio missing" marker (sample phrase p-1's version pv-1, flagged broken).</summary>
    [ScreenshotFact]
    public void PhraseVersionsDialog_brokenVersion()
    {
        var host = SampleHost();
        host.BrokenVersionIds = ["pv-1"];
        Save(() => new PhraseVersionsDialog { DataContext = new PhraseVersionsViewModel(host, host, host.Phrases[0]) },
            "phrase-versions-broken");
    }

    [ScreenshotFact]
    public void RepairPhraseDialog() =>
        Save(() => new RepairPhraseDialog { DataContext = new RepairPhraseViewModel(SampleHost().Phrases[0]) },
            "repair-phrase");

    // ---- setup-wizard steps (UserControls hosted in a bare window) ----

    [ScreenshotFact]
    public void WizardStep_environmentChecks() =>
        Save(() => WizardStep(0, new EnvironmentChecksStepView()), "wizard-1-environment-checks");

    [ScreenshotFact]
    public void WizardStep_calibration() =>
        Save(() => WizardStep(1, new CalibrationStepView()), "wizard-2-calibration");

    [ScreenshotFact]
    public void WizardStep_hotkeyStatus() =>
        Save(() => WizardStep(2, new HotkeyStatusStepView()), "wizard-3-hotkey-status");

    [ScreenshotFact]
    public void WizardStep_instruction() =>
        Save(() => WizardStep(3, new InstructionStepView()), "wizard-4-instruction");

    [ScreenshotFact]
    public void WizardStep_firstCall() =>
        Save(() => WizardStep(4, new FirstCallStepView()), "wizard-5-first-call");

    // ---- helpers ----

    // Light screenshots land in a separate folder so a dark run doesn't overwrite them.
    private static readonly string Group =
        Environment.GetEnvironmentVariable("ADAVOICE_SCREENSHOT_THEME") == "Light" ? "after-light" : "after";

    private void Save(Func<Window> build, string name) =>
        Assert.True(File.Exists(_harness.Capture(build, name, Group)));

    /// <summary>Wraps a wizard step's <see cref="UserControl"/> in a bare Fluent window, wired to a
    /// fresh wizard's matching step view-model.</summary>
    private static Window WizardStep(int index, UserControl view)
    {
        var wizard = new SetupWizardViewModel(SampleHost(), "Pause");
        view.DataContext = wizard.Steps[index];
        return new Wpf.Ui.Controls.FluentWindow
        {
            Content = view,
            Width = 480,
            Height = 560,
            Background = (Brush)Application.Current.Resources["Surface.Window"],
        };
    }

    private static BoardViewModel NewBoard(EngineState? state = null)
    {
        var host = SampleHost();
        if (state is { } s)
            host.State = s;
        return NewBoardFor(host);
    }

    private static BoardViewModel NewWideBoard()
    {
        var host = SampleHost();
        host.Phrases =
        [
            .. host.Phrases,
            // 3 tags (cap is 1 — see PhraseItemViewModel.MaxVisibleTagChips) exercises the "+N"
            // overflow chip; the long title exercises the 2-line clamp + ellipsis (Phase B). Lives
            // only here, not on SampleHost's p-1 — see NewBoard()'s p-1 comment for why.
            new PhraseEntry { Id = "p-5", Title = "Booking a follow-up call — thanks so much for calling us today", CategoryId = "c-greet", Tags = ["vip", "opening", "friendly"], DurationMs = 4800 },
            new PhraseEntry { Id = "p-6", Title = "Explaining the refund policy", CategoryId = Category.DefaultId, DurationMs = 6100 },
            new PhraseEntry { Id = "p-7", Title = "Objection: need to think about it", CategoryId = Category.DefaultId, Tags = ["friendly"], DurationMs = 3900 },
            new PhraseEntry { Id = "p-8", Title = "Escalating to a manager", CategoryId = "c-close", DurationMs = 2900 },
            new PhraseEntry { Id = "p-9", Title = "Confirming the appointment time", CategoryId = "c-greet", DurationMs = 3300 },
            new PhraseEntry { Id = "p-10", Title = "Wrapping up the call", CategoryId = "c-close", DurationMs = 2400 },
        ];
        return NewBoardFor(host);
    }

    private static BoardViewModel NewBoardFor(FakePlaybackHost host)
    {
        var settingsHost = new FakeSettingsHost();
        return new BoardViewModel(host, host, host, host, settingsHost,
            new StatusViewModel(host), new SettingsViewModel(settingsHost));
    }

    private static SettingsWindowViewModel NewSettings() =>
        new(new FakeSettingsHost { Language = "en" }, SampleHost(), "Pause",
            pickExportPath: () => null,
            pickImportFile: () => Task.FromResult<(string Path, ImportMode Mode)?>(null),
            confirmAndRestart: () => Task.CompletedTask,
            showError: _ => Task.CompletedTask,
            showInfo: _ => Task.CompletedTask);

    /// <summary>A host pre-loaded with representative categories, tags, phrases, and one conversation
    /// so the screenshots show a populated UI rather than empty states.</summary>
    private static FakePlaybackHost SampleHost() => new()
    {
        State = EngineState.Live,
        Categories =
        [
            new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" },
            new Category { Id = "c-greet", Name = "Greetings", Color = "#4CC2FF" },
            new Category { Id = "c-close", Name = "Closing", Color = "#7A7CFF" },
        ],
        Tags =
        [
            new TagInfo { Name = "opening", Color = "#4CC2FF" },
            new TagInfo { Name = "friendly", Color = "#57C7A6" },
            new TagInfo { Name = "vip", Color = "#E8B04B" },
        ],
        Phrases =
        [
            new PhraseEntry
            {
                // Kept short and 2-tag on purpose: p-1 is Phrases[0], reused by several OTHER
                // dialogs' screenshots (PhraseEditDialog, PhraseVersionsDialog, ManageConversations
                // — it's a "Cold call" member). A long title + 3rd tag here once distorted those
                // dialogs' layouts (a row's text ran behind its buttons) even though it correctly
                // exercised MainWindow's tile clamp/overflow — that stress data now lives only in
                // NewWideBoard's p-5, which no other dialog's fixture touches.
                Id = "p-1", Title = "Warm intro", CategoryId = "c-greet",
                Tags = ["opening", "friendly"], DurationMs = 3200,
                Versions = [new PhraseVersion { Id = "pv-1", Label = "Energetic", DurationMs = 3100 }],
            },
            new PhraseEntry { Id = "p-2", Title = "Pricing overview", CategoryId = Category.DefaultId, DurationMs = 5400 },
            new PhraseEntry { Id = "p-3", Title = "Objection: too expensive", CategoryId = Category.DefaultId, DurationMs = 4100 },
            new PhraseEntry { Id = "p-4", Title = "Thanks & next steps", CategoryId = "c-close", DurationMs = 2600 },
        ],
        Conversations = [new Conversation { Id = "v-1", Name = "Cold call", PhraseIds = ["p-1", "p-2", "p-4"] }],
        NextChecks =
        [
            new EnvironmentCheck(EnvironmentCheckKind.CableOutput, CheckStatus.Pass, FoundName: "VB-Audio Cable"),
            new EnvironmentCheck(EnvironmentCheckKind.Microphone, CheckStatus.Pass, FoundName: "Default microphone"),
            new EnvironmentCheck(EnvironmentCheckKind.DefaultOutput, CheckStatus.Fail, FoundName: "VB-Audio Cable"),
        ],
    };
}
