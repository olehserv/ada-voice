using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// Owns the single WPF <see cref="Application"/> and one STA "UI thread" for the whole test run.
/// Windows are built and shown on <see cref="Dispatcher"/>; screenshots are taken from the xunit
/// thread while this thread keeps pumping messages — so same-process UI Automation never deadlocks
/// (the deadlock only happens when the UI-owning thread makes blocking automation calls).
/// </summary>
/// <remarks>
/// It builds the real <see cref="App"/> resources (Fluent theme, brand tokens, converters) so
/// <c>DynamicResource</c>/<c>StaticResource</c> lookups resolve exactly as in the running app.
/// <c>OnStartup</c> does run here (confirmed empirically: merely running
/// <see cref="Dispatcher.Run()"/> on this thread is enough for WPF to raise <c>Startup</c>, with
/// no explicit <see cref="Application.Run()"/> call needed) — that is pre-existing, harmless-for-
/// tests behavior this fixture has always relied on (its real <c>ApplyTheme</c> call is what lets
/// WPF-UI's pack:// resource resolution work at all on this thread). <see cref="App.
/// SkipLanguageForTests"/> only gates the one line that would otherwise fight this fixture's
/// English pin below with whatever language the real <c>settings.json</c> has. Only one
/// <see cref="Application"/> may exist per process, so this is a shared collection fixture.
/// </remarks>
public sealed class WpfAppFixture : IDisposable
{
    private readonly Thread _uiThread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Dispatcher? _dispatcher;

    public WpfAppFixture()
    {
        Theme = Environment.GetEnvironmentVariable("ADAVOICE_SCREENSHOT_THEME") == "Light"
            ? Wpf.Ui.Appearance.ApplicationTheme.Light
            : Wpf.Ui.Appearance.ApplicationTheme.Dark;

        _uiThread = new Thread(Pump) { IsBackground = true, Name = "WpfScreenshotUI" };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _ready.Wait();
    }

    /// <summary>The dispatcher of the UI thread. Marshal all window work here.</summary>
    public Dispatcher Dispatcher => _dispatcher!;

    /// <summary>
    /// The theme chosen by <c>ADAVOICE_SCREENSHOT_THEME</c> (default dark). Closing a WPF-UI
    /// <c>FluentWindow</c> resets <c>ApplicationThemeManager</c> back to the OS theme as a side
    /// effect, so <see cref="ScreenshotHarness"/> re-applies this theme before building every
    /// window rather than relying on a single apply at startup.
    /// </summary>
    public Wpf.Ui.Appearance.ApplicationTheme Theme { get; }

    private void Pump()
    {
        // English regardless of the machine's real OS/keyboard language, so string assertions
        // stay verbatim-English (belt-and-suspenders alongside TestCultureInitializer's
        // assembly-wide pin). App.SkipLanguageForTests keeps OnStartup's own ApplyLanguage call
        // from overwriting this with whatever language the real settings.json has.
        var english = new CultureInfo("en-US");
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;
        App.SkipLanguageForTests = true;

        // new App() + InitializeComponent() loads App.xaml's resources without running the app.
        var app = new App();
        app.InitializeComponent();

        _dispatcher = Dispatcher.CurrentDispatcher;
        _ready.Set();
        Dispatcher.Run();
    }

    public void Dispose()
    {
        _dispatcher?.InvokeShutdown();
        _uiThread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }
}

/// <summary>Binds the screenshot tests to the one shared <see cref="WpfAppFixture"/>.</summary>
[CollectionDefinition(Name)]
public sealed class WpfAppCollection : ICollectionFixture<WpfAppFixture>
{
    public const string Name = "wpf-app";
}
