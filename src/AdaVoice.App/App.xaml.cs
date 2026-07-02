using System.IO;
using System.Windows;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Wasapi;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Log to a rolling file (same as the console host) so a blind GUI run is still diagnosable.
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "adavoice-.log");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        _host = new EngineHost(new WasapiAudioOptions(), msg => Log.Information("{Event}", msg));

        // BeginInvoke (async) so a state change raised on the engine control thread never blocks it on the UI.
        var status = new StatusViewModel(_host, action => Dispatcher.BeginInvoke(action));
        var settings = new SettingsViewModel(_host);

        var window = new MainWindow();
        var board = new BoardViewModel(
            _host, _host, _host, _host, status, settings,
            () => window.ActiveHotkey,
            action => Dispatcher.BeginInvoke(action),
            confirmDelete: window.ConfirmDelete,
            showEditDialog: window.ShowEditDialog,
            showManageCategories: window.ShowManageCategories,
            showSetupWizard: window.ShowSetupWizard);

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
        base.OnExit(e);
    }
}
