using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AdaVoice.App.Resources;
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

    /// <summary>
    /// Set by <c>WpfAppFixture</c> (screenshot/layout tests) before constructing <see cref="App"/>.
    /// Confirmed empirically that merely running <see cref="Dispatcher.Run()"/> on the thread that
    /// constructed an <see cref="Application"/> — with no explicit <see cref="Application.Run()"/>
    /// call — is enough for WPF to raise <c>Startup</c> and invoke this method anyway. That
    /// pre-existing behavior is otherwise harmless for tests (real <see cref="EngineHost"/>
    /// construction against the operator's real data is a known, separately-tracked gap — see
    /// handoff.md's open follow-ups), except for one thing this retrofit adds:
    /// <see cref="ApplyLanguage"/> would overwrite the fixture's English culture pin with whatever
    /// language the real <c>settings.json</c> happens to have (e.g. "uk"), breaking every
    /// verbatim-English string assertion in a screenshot/layout test. This flag gates only that
    /// one line — nothing else about <see cref="OnStartup"/> changes for tests.
    /// </summary>
    public static bool SkipLanguageForTests { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one instance may own the mic/cable devices, the log file, and the settings files.
        // A second instance would double the mic into the call and last-writer-win the JSON stores.
        var mutex = new Mutex(initiallyOwned: true, @"Local\AdaVoice.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            MessageBox.Show(Strings.App_AlreadyRunning, "AdaVoice",
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

        // Re-sync (swap tokens + accent) whenever the theme changes, using the theme the event hands
        // us — never re-apply the system theme here, or it would raise Changed and recurse. Wired
        // before the host loads so it's ready the moment ApplyThemePreference (below) applies anything.
        Wpf.Ui.Appearance.ApplicationThemeManager.Changed += (theme, _) => SyncBrandLayer(theme);

        _host = new EngineHost(new WasapiAudioOptions(), msg => Log.Information("{Event}", msg));

        // Localization (en/uk/pl), restart-to-apply (see Settings.Language's doc comment and
        // BackupSettingsViewModel.OnLanguageChanged's restart prompt) — so culture is set once,
        // here, before any resource lookup (theme apply below, then every ViewModel/window) can
        // happen. CurrentUICulture drives Strings.resx lookup; CurrentCulture drives number/date
        // formatting. Both are safe to set process-wide: the only culture-sensitive format in the
        // Core storage path is a fixed-digit timestamp, and JSON (de)serialization is invariant.
        // Skipped in tests (see SkipLanguageForTests) — WpfAppFixture pins English itself, and
        // this call would otherwise overwrite that pin with the real settings.json's language.
        if (!SkipLanguageForTests)
            ApplyLanguage(_host.Language);

        // Apply the persisted theme preference (design: manual theme setting). Default "system"
        // reproduces the app's original OS-follow behavior (design 10 redesign, 2026-07-11) for
        // anyone whose settings.json predates this field. Read here — before the window exists —
        // for the theme-only half (ApplyTheme); the window-dependent half (Watch/UnWatch) waits
        // until the window is built below.
        var themePreference = _host.Theme;
        var initialTheme = ResolveTheme(themePreference);
        ApplyTheme(initialTheme);

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
            showRepairDialog: window.ShowRepairDialog);

        window.DataContext = board;
        window.Show(); // triggers OnLoaded: wires Saved/Deleted AND registers the stop hotkey

        // WPF-UI's theme swap silently no-ops when it runs before the dispatcher is pumping (as the
        // ApplyTheme above did, inside OnStartup). Re-apply once the loop is live to guarantee the
        // Fluent chrome matches the preference at launch, and (only for "system") start following the
        // OS from here on — ApplyThemePreference does both, keyed off the same preference read above.
        Dispatcher.BeginInvoke(new Action(() => ApplyThemePreference(themePreference, window)));

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
    /// Sets the process culture from a persisted language code ("en"/"uk"/"pl"). Unrecognized or
    /// missing codes (a future bad value, or an old settings.json predating this field) degrade
    /// safely to English rather than throwing — <see cref="CultureInfo"/> would otherwise reject
    /// an unknown tag. Sets CurrentUICulture (drives every <c>{x:Static res:Strings.*}</c> lookup)
    /// and CurrentCulture (number/date formatting) before any window is built, so every
    /// resource-bound string resolves correctly from first paint — no live-switching needed given
    /// the restart-to-apply language model (see Settings.Language's doc comment).
    /// </summary>
    private static void ApplyLanguage(string code)
    {
        var culture = code switch
        {
            "uk" => new CultureInfo("uk-UA"),
            "pl" => new CultureInfo("pl-PL"),
            _ => new CultureInfo("en-US"),
        };

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
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
    /// Maps a persisted theme preference ("system"/"light"/"dark") to the concrete WPF-UI theme to
    /// apply right now. "system" (and anything unrecognized, so a future bad value degrades safely)
    /// resolves via the OS registry read, matching the app's original OS-follow behavior.
    /// </summary>
    private static Wpf.Ui.Appearance.ApplicationTheme ResolveTheme(string preference) => preference switch
    {
        "light" => Wpf.Ui.Appearance.ApplicationTheme.Light,
        "dark" => Wpf.Ui.Appearance.ApplicationTheme.Dark,
        _ => OsPrefersLightTheme()
            ? Wpf.Ui.Appearance.ApplicationTheme.Light
            : Wpf.Ui.Appearance.ApplicationTheme.Dark,
    };

    /// <summary>
    /// Applies a theme preference end to end: resolves it to a concrete theme, applies it, and starts
    /// or stops following OS changes to match. Only a fixed Light/Dark choice skips watching — it must
    /// NOT silently flip when the operator changes their Windows theme. Everything else (including
    /// "system" and any unrecognized value — see <see cref="ResolveTheme"/>) watches, so the two stay
    /// consistent. <see cref="Wpf.Ui.Appearance.SystemThemeWatcher"/>'s <c>Watch</c> only reacts to
    /// <i>future</i> OS changes, not the current one, so switching back to "system" always resolves
    /// and applies first — otherwise the app would look stuck on the last fixed theme until the OS
    /// setting happens to change again. <c>UnWatch</c> runs unconditionally first (it's a safe no-op
    /// on a window that isn't watched) because <c>Watch</c> itself does not dedupe: calling it twice
    /// on the same window stacks a second WndProc hook rather than replacing the first, which
    /// re-selecting "system" more than once in a session would otherwise trigger.
    /// </summary>
    public static void ApplyThemePreference(string preference, Window window)
    {
        ApplyTheme(ResolveTheme(preference));

        Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(window);
        if (preference is not ("light" or "dark"))
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(
                window, Wpf.Ui.Controls.WindowBackdropType.None, updateAccents: false);
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
                string.Format(Strings.App_CrashMessageFormat, args.Exception.Message),
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
