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
/// <c>DynamicResource</c>/<c>StaticResource</c> lookups resolve exactly as in the running app, but
/// never calls <see cref="Application.Run()"/> — so <c>OnStartup</c> (the single-instance mutex,
/// WASAPI, the <c>EngineHost</c>) never fires. Only one <see cref="Application"/> may exist per
/// process, so this is a shared collection fixture.
/// </remarks>
public sealed class WpfAppFixture : IDisposable
{
    private readonly Thread _uiThread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Dispatcher? _dispatcher;

    public WpfAppFixture()
    {
        _uiThread = new Thread(Pump) { IsBackground = true, Name = "WpfScreenshotUI" };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _ready.Wait();
    }

    /// <summary>The dispatcher of the UI thread. Marshal all window work here.</summary>
    public Dispatcher Dispatcher => _dispatcher!;

    private void Pump()
    {
        // new App() + InitializeComponent() loads App.xaml's resources without running the app.
        // Application.Run() (and therefore OnStartup) is never called.
        var app = new App();
        app.InitializeComponent();

        // Brand accent — App.OnStartup does this, and it is not part of the XAML resources.
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
            System.Windows.Media.Color.FromRgb(0x4C, 0xC2, 0xFF),
            Wpf.Ui.Appearance.ApplicationTheme.Dark);

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
