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

        // Brand accent (design 09): without this, WPF-UI derives Primary buttons, checkboxes, and
        // focus visuals from the OS accent color — whatever the user picked in Windows settings.
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
            System.Windows.Media.Color.FromRgb(0x4C, 0xC2, 0xFF),
            Wpf.Ui.Appearance.ApplicationTheme.Dark);

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
