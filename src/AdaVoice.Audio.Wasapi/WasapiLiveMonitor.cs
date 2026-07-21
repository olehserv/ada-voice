using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Playback;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// NAudio also has a DeviceState type; use ours for the seam state.
using DeviceState = AdaVoice.Audio.Abstractions.DeviceState;

namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Production <see cref="ILiveMonitor"/>: renders a phrase's samples to a second, independent
/// WASAPI output while the same phrase plays to the call. Same shape as
/// <c>EngineHost.Preview</c> (resolve device → cable guard → build the sample chain → a
/// <see cref="WasapiRenderDevice"/>), except it never blocks the caller — the whole render
/// happens on a background task, since this is driven from the engine's own event, not a
/// user-initiated preview action.
/// </summary>
public sealed class WasapiLiveMonitor : ILiveMonitor
{
    private readonly Func<MMDevice> _resolveDevice;
    private readonly string _cableName;
    private readonly Action<string> _log;
    private readonly Lock _sync = new();
    private WasapiRenderDevice? _render;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <param name="resolveDevice">Resolves the current monitor output (the configured device,
    /// falling back to the OS default) — called fresh on every <see cref="Start"/> so a device
    /// change between phrases is picked up.</param>
    /// <param name="cableName">Friendly-name substring of the call cable — never rendered to.</param>
    /// <param name="log">Plain log callback, matching the rest of the host.</param>
    public WasapiLiveMonitor(Func<MMDevice> resolveDevice, string cableName, Action<string> log)
    {
        _resolveDevice = resolveDevice;
        _cableName = cableName;
        _log = log;
    }

    public void Start(float[] samples, double volume)
    {
        StopCurrent();

        CancellationTokenSource cts;
        lock (_sync)
        {
            if (_disposed)
                return;
            cts = _cts = new CancellationTokenSource();
        }

        Task.Run(() => Run(samples, volume, cts));
    }

    public void Stop() => StopCurrent();

    private void Run(float[] samples, double volume, CancellationTokenSource cts)
    {
        WasapiRenderDevice? render = null;
        try
        {
            var device = _resolveDevice();

            // Cardinal rule (same as Preview): never feed the take toward the call. If the
            // monitor resolves to the cable, refuse rather than double the call's audio.
            if (device.FriendlyName.Contains(_cableName, StringComparison.OrdinalIgnoreCase))
            {
                device.Dispose();
                _log("live monitor refused: resolves to the cable");
                return;
            }

            if (cts.IsCancellationRequested)
            {
                device.Dispose();
                return; // stopped/replaced while we were still resolving the device
            }

            // AudioClient is a fresh RCW per access — dispose it (one leak per start otherwise).
            int deviceRate;
            using (var audioClient = device.AudioClient)
                deviceRate = audioClient.MixFormat.SampleRate;

            ISampleProvider source = new PhraseSampleProvider(samples, AudioFormats.Engine, "monitor");
            source = new VolumeSampleProvider(source) { Volume = (float)volume };
            if (deviceRate != AudioFormats.SampleRate)
                source = new WdlResamplingSampleProvider(source, deviceRate);

            render = new WasapiRenderDevice(device, optOutOfDucking: false);

            using var done = new ManualResetEventSlim(false);
            render.StateChanged += (_, e) =>
            {
                if (e.State is DeviceState.Stopped or DeviceState.Faulted)
                    done.Set();
            };

            lock (_sync)
            {
                if (cts.IsCancellationRequested || _disposed)
                {
                    // Stopped/replaced/disposed while we built the graph — tear down unplayed.
                    render.Dispose();
                    return;
                }

                _render = render;
            }

            render.Init(source);
            render.Start();

            var durationMs = samples.Length * 1000L / AudioFormats.SampleRate;
            done.Wait(TimeSpan.FromMilliseconds(durationMs + 1000)); // backstop; Stop() can cut it short
        }
        catch (Exception ex)
        {
            _log($"live monitor error: {ex.Message}");
        }
        finally
        {
            // Never dispose from inside the StateChanged callback that requested the wait (that
            // callback's own thread already returned once done.Wait unblocked) — this runs on
            // this task's own thread, so it is safe. Idempotent: WasapiRenderDevice.Stop/Dispose
            // both no-op if a concurrent StopCurrent() already tore this render down.
            lock (_sync)
            {
                if (ReferenceEquals(_render, render))
                    _render = null;
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
            }

            render?.Stop();
            render?.Dispose();
            cts.Dispose();
        }
    }

    // Stop() only — never Dispose() here. StopCurrent() can run on the engine's own threads (a
    // natural phrase end reaches it via PhraseChanged, raised from the mixer's render thread under
    // its lock — see EngineHost.OnEngineEvent's remarks), and WasapiOut.Dispose() joins its
    // playback thread, a blocking wait this codebase deliberately keeps off any audio thread
    // (mirrors EngineHost.StopPreview, which is Stop()-only for the same reason). The render's own
    // Run() task owns the actual Dispose() — it wakes from done.Wait once Stop() completes
    // asynchronously and disposes on its own background thread in its finally block.
    private void StopCurrent()
    {
        CancellationTokenSource? cts;
        WasapiRenderDevice? render;
        lock (_sync)
        {
            cts = _cts;
            _cts = null;
            render = _render;
            _render = null;
        }

        cts?.Cancel();
        render?.Stop();
    }

    public void Dispose()
    {
        lock (_sync)
            _disposed = true;
        StopCurrent();
    }
}
