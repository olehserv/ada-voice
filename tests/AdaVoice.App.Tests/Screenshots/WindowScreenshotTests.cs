using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Setup;
using AdaVoice.Core.Domain;

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

    [ScreenshotFact]
    public void SettingsWindow() =>
        Save(() => new SettingsWindow { DataContext = NewSettings() }, "settings");

    [ScreenshotFact]
    public void SetupWizardWindow() =>
        Save(() => new SetupWizardWindow { DataContext = new SetupWizardViewModel(SampleHost(), "Pause") },
            "setup-wizard");

    [ScreenshotFact]
    public void RecorderDialog() =>
        Save(() => new RecorderDialog { DataContext = NewBoard() }, "recorder");

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

    private static BoardViewModel NewBoard()
    {
        var host = SampleHost();
        var settingsHost = new FakeSettingsHost();
        return new BoardViewModel(host, host, host, host, settingsHost,
            new StatusViewModel(host), new SettingsViewModel(settingsHost));
    }

    private static BoardViewModel NewWideBoard()
    {
        var host = SampleHost();
        host.Phrases =
        [
            .. host.Phrases,
            new PhraseEntry { Id = "p-5", Title = "Booking a follow-up call", CategoryId = "c-greet", Tags = ["opening"], DurationMs = 4800 },
            new PhraseEntry { Id = "p-6", Title = "Explaining the refund policy", CategoryId = Category.DefaultId, DurationMs = 6100 },
            new PhraseEntry { Id = "p-7", Title = "Objection: need to think about it", CategoryId = Category.DefaultId, Tags = ["friendly"], DurationMs = 3900 },
            new PhraseEntry { Id = "p-8", Title = "Escalating to a manager", CategoryId = "c-close", DurationMs = 2900 },
            new PhraseEntry { Id = "p-9", Title = "Confirming the appointment time", CategoryId = "c-greet", DurationMs = 3300 },
            new PhraseEntry { Id = "p-10", Title = "Wrapping up the call", CategoryId = "c-close", DurationMs = 2400 },
        ];
        var settingsHost = new FakeSettingsHost();
        return new BoardViewModel(host, host, host, host, settingsHost,
            new StatusViewModel(host), new SettingsViewModel(settingsHost));
    }

    private static SettingsWindowViewModel NewSettings() =>
        new(new FakeSettingsHost { Language = "en" }, SampleHost(), "Pause",
            pickExportPath: () => null,
            pickImportFile: () => null,
            confirmAndRestart: () => { },
            showError: _ => { },
            showInfo: _ => { });

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
        ],
        Phrases =
        [
            new PhraseEntry
            {
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
            new EnvironmentCheck("Virtual cable installed", CheckStatus.Pass, "VB-Audio Cable detected"),
            new EnvironmentCheck("Microphone available", CheckStatus.Pass, "Default microphone ready"),
            new EnvironmentCheck("Output routed to the cable", CheckStatus.Fail, "Set your meeting app output to the cable"),
        ],
    };
}
