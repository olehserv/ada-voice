using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Playback;
using AdaVoice.Audio.Wasapi;
using NAudio.CoreAudioApi;

namespace AdaVoice.Host;

/// <summary>
/// Composition root for the audio engine. It builds the real WASAPI device factory, the device
/// monitor, and the system clock, wires them into an <see cref="AudioEngine"/>, and runs the
/// engine's single control loop on a dedicated thread. The future WPF app reuses this class; only
/// the console <c>Program</c> around it is throwaway. Logging is a plain callback, so this class
/// depends on no logging library.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly WasapiAudioOptions _options;
    private readonly Action<string> _log;
    private readonly WasapiDeviceMonitor _monitor;
    private readonly AudioEngine _engine;
    private readonly Thread _controlThread;
    private volatile bool _running;

    public EngineHost(WasapiAudioOptions options, Action<string>? log = null)
    {
        _options = options;
        _log = log ?? (_ => { });

        var clock = new SystemEngineClock();
        var factory = new WasapiDeviceFactory(options);
        _monitor = new WasapiDeviceMonitor();
        _engine = new AudioEngine(factory, clock);

        _engine.Events += OnEngineEvent;
        _monitor.DeviceChanged += OnDeviceChanged;

        _controlThread = new Thread(ControlLoop) { Name = "AudioEngineControl", IsBackground = true };
    }

    public EngineState State => _engine.State;

    public void Start()
    {
        if (!_running)
        {
            _running = true;
            _controlThread.Start();
            _monitor.Start();
        }

        _engine.Start();
    }

    public void Stop() => _engine.Stop();
    public void Play(Phrase phrase) => _engine.Play(phrase);
    public void EnterOffAir() => _engine.EnterOffAir();
    public void ExitOffAir() => _engine.ExitOffAir();

    /// <summary>The single thread every command handler runs on. The clock timer and the device
    /// monitor only enqueue; all state transitions happen here.</summary>
    private void ControlLoop()
    {
        while (_running)
        {
            try
            {
                _engine.TryProcessNext(timeoutMs: 100);
            }
            catch (Exception ex)
            {
                // Belt-and-suspenders: no handler exception may ever kill the control thread.
                _log($"control loop error: {ex}");
            }
        }
    }

    private void OnEngineEvent(object? sender, EngineEvent e) => _log(Describe(e));

    private static string Describe(EngineEvent e) => e switch
    {
        EngineEvent.StateChanged s => $"state -> {s.State}{(s.Error is null ? "" : $" ({s.Error})")}",
        EngineEvent.DriftLogged d => $"drift: {d.Kind}",
        EngineEvent.RebuildResult r => $"rebuild {r.Role}: {(r.Success ? "ok" : "failed")} (attempt {r.Attempt})",
        _ => e.ToString() ?? string.Empty,
    };

    private void OnDeviceChanged(object? sender, DeviceChangeEventArgs e)
    {
        // Best-effort mapping (pragmatic v1): classify the device's flow -> role. The engine ignores
        // anything not currently relevant, so a coarse guess is safe. Device-loss recovery does not
        // rely on this (the seam fault callbacks already drive it); this mainly speeds up replug.
        if (RoleOf(e.DeviceId) is { } role)
        {
            _log($"device {e.Kind}: {role}");
            _engine.Post(new EngineCommand.DeviceChanged(role, e.Kind));
        }
    }

    private DeviceRole? RoleOf(string deviceId)
    {
        // A removed device no longer resolves, so we cannot classify it — that is fine, the engine's
        // fault path already handles removal. We mainly need to classify present devices (arrived /
        // new default) for the fast-path rebuild.
        using var device = WasapiDevices.ById(deviceId);
        if (device is null)
            return null;

        return device.DataFlow switch
        {
            DataFlow.Capture when _options.MicName is null
                || device.FriendlyName.Contains(_options.MicName, StringComparison.OrdinalIgnoreCase)
                => DeviceRole.Mic,

            DataFlow.Render when device.FriendlyName.Contains(_options.CableName, StringComparison.OrdinalIgnoreCase)
                => DeviceRole.Cable,

            _ => null,
        };
    }

    public void Dispose()
    {
        // Order matters: stop the loop, then stop the monitor (its COM callbacks Post), then dispose
        // the engine (which disposes the watchdog timer before the queue). This keeps a late Post off
        // a disposed queue; AudioEngine.Post also guards the remaining in-flight race.
        _running = false;
        if (_controlThread.IsAlive)
            _controlThread.Join(TimeSpan.FromSeconds(2));

        _monitor.DeviceChanged -= OnDeviceChanged;
        _monitor.Dispose();

        _engine.Events -= OnEngineEvent;
        _engine.Dispose();
    }
}
