using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Wasapi;
using AdaVoice.Core.Storage;
using AdaVoice.Host;
using Serilog;

namespace AdaVoice.App;

/// <summary>
/// Composition root for the WPF app: builds the reusable <see cref="EngineHost"/>, wires the
/// view-models (marshalling engine events onto the UI thread), and shows the Board.
/// </summary>
public partial class App : Application
{
    private EngineHost? _host;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one instance may own the mic/cable devices, the log file, and the settings files.
        // A second instance would double the mic into the call and last-writer-win the JSON stores.
        var mutex = new Mutex(initiallyOwned: true, @"Local\AdaVoice.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            MessageBox.Show("AdaVoice is already running.", "AdaVoice",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _singleInstanceMutex = mutex;

        // Relaunch after a crash: the mic-forwarding process must not stay dead (design 03).
        NativeMethods.RegisterApplicationRestart(null, 0);

        // Log to the data root, not the install dir (Program Files is not user-writable), so a
        // blind GUI run is still diagnosable in a deployed install (same location as user data).
        var logPath = Path.Combine(AdaVoicePaths.DefaultRoot, "logs", "adavoice-.log");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        RegisterGlobalExceptionHandlers();

        // Follow the OS light/dark theme, and keep following it while running (design 10 redesign
        // 2026-07-11 — the app was dark-only before). Read the OS "apps" theme straight from the
        // registry: it's unambiguous and works here, before the message loop runs. ApplyTheme swaps
        // WPF-UI's Fluent brushes AND our brand token dictionary + accent to match. (Runtime changes
        // are handled by SystemThemeWatcher below, which uses WPF-UI's own detection.)
        var osTheme = OsPrefersLightTheme()
            ? Wpf.Ui.Appearance.ApplicationTheme.Light
            : Wpf.Ui.Appearance.ApplicationTheme.Dark;
        ApplyTheme(osTheme);
        // Re-sync (swap tokens + accent) whenever the theme changes, using the theme the event hands
        // us — never re-apply the system theme here, or it would raise Changed and recurse.
        Wpf.Ui.Appearance.ApplicationThemeManager.Changed += (theme, _) => SyncBrandLayer(theme);

        _host = new EngineHost(new WasapiAudioOptions(), msg => Log.Information("{Event}", msg));

        // BeginInvoke (async) so a state change raised on the engine control thread never blocks it on the UI.
        var status = new StatusViewModel(_host, action => Dispatcher.BeginInvoke(action));
        var settings = new SettingsViewModel(_host);

        var window = new MainWindow { Topmost = _host.AlwaysOnTop };
        var board = new BoardViewModel(
            _host, _host, _host, _host, _host, status, settings,
            () => window.ActiveHotkey,
            action => Dispatcher.BeginInvoke(action),
            confirmDelete: window.ConfirmDelete,
            showEditDialog: window.ShowEditDialog,
            showVersionsDialog: window.ShowVersionsDialog,
            showManageCategories: window.ShowManageCategories,
            showManageConversations: window.ShowManageConversations,
            showRecorder: window.ShowRecorder,
            showSetupWizard: window.ShowSetupWizard,
            showSettings: window.ShowSettings,
            pickExportPath: window.PickExportPath,
            pickImportFile: window.PickImportFile,
            confirmAndRestart: window.ConfirmAndRestart,
            showError: window.ShowError,
            showSettingsInfo: window.ShowInfo,
            showRepairDialog: window.ShowRepairDialog);

        window.DataContext = board;
        window.Show(); // triggers OnLoaded: wires Saved/Deleted AND registers the stop hotkey

        // WPF-UI's theme swap silently no-ops when it runs before the dispatcher is pumping (as the
        // ApplyTheme above did, inside OnStartup). Re-apply once the loop is live to guarantee the
        // Fluent chrome matches the OS at launch; then follow OS changes. None = keep the window's own
        // backdrop (design 09 flat surfaces); updateAccents:false = keep our brand accent.
        Dispatcher.BeginInvoke(new Action(() => ApplyTheme(osTheme)));
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(
            window, Wpf.Ui.Controls.WindowBackdropType.None, updateAccents: false);

        // First run: window.ActiveHotkey is only valid after Show() (OnLoaded already ran).
        if (!settings.WizardCompleted)
            window.ShowSetupWizard(new SetupWizardViewModel(_host, window.ActiveHotkey));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Reads the OS "apps use light theme" preference straight from the registry. Synchronous and
    /// reliable before the message loop runs (unlike WPF-UI's theme apply, which no-ops pre-pump).
    /// Missing key / any error ⇒ dark (the app's historical default).
    /// </summary>
    private static bool OsPrefersLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies a specific theme and re-syncs the brand layer (tokens + accent). Startup derives the
    /// theme from the OS and calls this; the screenshot tests call it to render each theme. It only
    /// fully takes effect once the dispatcher is pumping, so startup also re-applies it via
    /// <c>BeginInvoke</c>. Backdrop stays None — design 09's flat surfaces.
    /// </summary>
    public static void ApplyTheme(Wpf.Ui.Appearance.ApplicationTheme theme)
    {
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            theme, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
        SyncBrandLayer(theme);
    }

    /// <summary>
    /// Applies our brand layer for <paramref name="theme"/>: swaps the light/dark token dictionary
    /// and re-derives the WPF-UI accent from the (theme-specific) <c>Accent</c> brush. The accent
    /// colour is therefore defined once, in XAML, and never duplicated in code. The theme is passed
    /// in (not read back via <c>GetAppTheme</c>) so it stays correct even before the app is running.
    /// </summary>
    private static void SyncBrandLayer(Wpf.Ui.Appearance.ApplicationTheme theme)
    {
        SwapBrandTokens(theme);
        if (Current.Resources["Accent"] is System.Windows.Media.SolidColorBrush accent)
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(accent.Color, theme);
    }

    /// <summary>
    /// Merges the token dictionary matching <paramref name="theme"/> and removes the other, so the
    /// <c>DynamicResource</c> lookups in every view re-resolve to the right palette at runtime.
    /// </summary>
    private static void SwapBrandTokens(Wpf.Ui.Appearance.ApplicationTheme theme)
    {
        var file = theme == Wpf.Ui.Appearance.ApplicationTheme.Light ? "Tokens.Light.xaml" : "Tokens.Dark.xaml";
        var dicts = Current.Resources.MergedDictionaries;
        var alreadyCorrect = false;

        for (var i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.OriginalString ?? string.Empty;
            if (!src.Contains("Tokens.Dark") && !src.Contains("Tokens.Light"))
                continue;
            if (src.Contains(file))
                alreadyCorrect = true;
            else
                dicts.RemoveAt(i);
        }

        if (!alreadyCorrect)
        {
            // Absolute pack URI (with the assembly name) so it resolves from AdaVoice.App's compiled
            // resources even when the entry assembly is something else (e.g. the test host).
            var uri = new Uri($"pack://application:,,,/AdaVoice.App;component/Theme/{file}", UriKind.Absolute);
            dicts.Add(new ResourceDictionary { Source = uri });
        }
    }

    /// <summary>
    /// A crash must never be silent (the log's stated purpose is "a blind GUI run is still
    /// diagnosable"). UI-thread errors are logged, shown, and swallowed — for this product
    /// "log, tell, keep running" beats killing the operator's mic path mid-call. Errors that
    /// are already tearing the process down are logged and flushed so the file survives.
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "Unhandled UI exception");
            MessageBox.Show(
                $"Something went wrong: {args.Exception.Message}\n\nAdaVoice keeps running; details are in the log.",
                "AdaVoice", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception — process is terminating");
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }
}
