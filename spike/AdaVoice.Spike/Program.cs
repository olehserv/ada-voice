using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Spike;

/// <summary>
/// Phase 0 spike (THROWAWAY — see docs/roadmaps/mvp-roadmap.md):
/// mic -> duck -> mixer -> CABLE Input passthrough with WAV phrase mixing,
/// communications-ducking opt-out, and latency measurement hooks.
/// </summary>
public static class Program
{
    private const double DefaultDuckDb = -12;
    private const int DuckRampMs = 50;

    private static MixingSampleProvider? _mixer;
    private static RampGain? _micGain;
    private static PhraseSampleProvider? _activePhrase;
    private static readonly object Sync = new();
    private static bool _duckEnabled = true;
    private static double _duckDb = DefaultDuckDb;
    private static long _overruns;

    public static int Main(string[] args)
    {
        using var enumerator = new MMDeviceEnumerator();

        if (args.Contains("--list"))
        {
            ListDevices(enumerator);
            return 0;
        }

        var micFilter = ArgValue(args, "--mic");
        var cableFilter = ArgValue(args, "--cable") ?? "CABLE Input";
        var phraseDir = ArgValue(args, "--phrases")
            ?? Path.Combine(AppContext.BaseDirectory, "phrases");

        // --- Resolve devices -------------------------------------------------
        var mic = micFilter is null
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            : FindDevice(enumerator, DataFlow.Capture, micFilter);
        var cableIn = FindDevice(enumerator, DataFlow.Render, cableFilter);
        var cableOut = TryFindDevice(enumerator, DataFlow.Capture, "CABLE Output");

        if (mic is null) { Console.Error.WriteLine($"Mic not found: '{micFilter}'. Run with --list."); return 1; }
        if (cableIn is null)
        {
            Console.Error.WriteLine($"Render device '{cableFilter}' not found. Is VB-CABLE installed?");
            Console.Error.WriteLine("Download: https://vb-audio.com/Cable/ (do not redistribute — donationware, design 02 §6)");
            return 1;
        }

        Console.WriteLine($"Mic:    {mic.FriendlyName}  ({mic.AudioClient.MixFormat.SampleRate} Hz)");
        Console.WriteLine($"Cable:  {cableIn.FriendlyName}  ({cableIn.AudioClient.MixFormat.SampleRate} Hz)");
        if (cableIn.AudioClient.MixFormat.SampleRate != 48000)
            Console.WriteLine("  WARNING: cable is not at 48 kHz — set both CABLE endpoints to 48 kHz in Sound settings (wizard check, roadmap).");

        // --- Phrases ----------------------------------------------------------
        var phrases = PhraseCache.Load(phraseDir);
        for (var i = 0; i < phrases.Count; i++)
            Console.WriteLine($"  [{i + 1}] {phrases[i].Name} ({phrases[i].Data.Length / 48000.0:F1}s)");

        // --- Graph: capture -> buffer -> 48k mono float -> duck -> mixer ------
        using var capture = new WasapiCapture(mic, true, 20);
        var buffered = new BufferedWaveProvider(capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true,
        };
        capture.DataAvailable += (_, e) =>
        {
            // Drift policy (spike version of design 06 §1): drop-oldest at >100 ms backlog.
            if (buffered.BufferedDuration.TotalMilliseconds > 100)
            {
                Interlocked.Increment(ref _overruns);
                buffered.ClearBuffer();
            }
            buffered.AddSamples(e.Buffer, 0, e.BytesRecorded);
        };

        ISampleProvider micChain = buffered.ToSampleProvider();
        if (micChain.WaveFormat.Channels == 2)
            micChain = micChain.ToMono(0.5f, 0.5f);
        else if (micChain.WaveFormat.Channels > 2)
        {
            Console.Error.WriteLine($"Mic has {micChain.WaveFormat.Channels} channels — pick a stereo/mono device with --mic.");
            return 1;
        }
        if (micChain.WaveFormat.SampleRate != 48000)
            micChain = new WdlResamplingSampleProvider(micChain, 48000);
        _micGain = new RampGain(micChain);

        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)) { ReadFully = true };
        _mixer.AddMixerInput(_micGain);
        _mixer.MixerInputEnded += (_, e) =>
        {
            lock (Sync)
            {
                if (ReferenceEquals(e.SampleProvider, _activePhrase))
                {
                    _activePhrase = null;
                    _micGain!.SetTarget(1f, DuckRampMs); // un-duck
                }
            }
        };

        using var output = new WasapiOut(cableIn, AudioClientShareMode.Shared, true, 20);
        output.Init(new MonoToStereoSampleProvider(_mixer).ToWaveProvider());

        capture.StartRecording();
        output.Play();

        // Opt out of communications ducking — AFTER stream start (design 06 §1).
        try
        {
            DuckingOptOut.Apply(cableIn.ID);
            Console.WriteLine("Ducking opt-out applied to the cable render session.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: ducking opt-out failed ({ex.Message}).");
            Console.WriteLine("  Fallback: Sound -> Communications -> 'Do nothing' (design 02 §3).");
        }

        Console.WriteLine();
        Console.WriteLine("LIVE — passthrough running. In Chrome, pick 'CABLE Output' as the microphone.");
        Console.WriteLine("Keys: [1-9] play phrase  [S]top  [D]uck on/off  [+/-] duck dB  [L]atency self-test  [I]nfo  [Q]uit");

        // --- Console loop -----------------------------------------------------
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Q) break;

            switch (key.Key)
            {
                case >= ConsoleKey.D1 and <= ConsoleKey.D9:
                    var index = key.Key - ConsoleKey.D1;
                    if (index < phrases.Count) Trigger(phrases[index].Data, phrases[index].Name);
                    break;
                case ConsoleKey.S:
                    StopPhrase();
                    break;
                case ConsoleKey.D:
                    _duckEnabled = !_duckEnabled;
                    Console.WriteLine($"Ducking: {(_duckEnabled ? $"ON ({_duckDb:F0} dB)" : "OFF")}");
                    break;
                case ConsoleKey.OemPlus or ConsoleKey.Add:
                    _duckDb = Math.Min(0, _duckDb + 3);
                    Console.WriteLine($"Duck level: {_duckDb:F0} dB");
                    break;
                case ConsoleKey.OemMinus or ConsoleKey.Subtract:
                    _duckDb = Math.Max(-60, _duckDb - 3);
                    Console.WriteLine($"Duck level: {_duckDb:F0} dB");
                    break;
                case ConsoleKey.L when cableOut is not null:
                    LatencyTest.Run(cableOut, p => { lock (Sync) _mixer!.AddMixerInput(p); });
                    break;
                case ConsoleKey.L:
                    Console.WriteLine("CABLE Output capture device not found — cannot self-test.");
                    break;
                case ConsoleKey.I:
                    Console.WriteLine($"buffered: {buffered.BufferedDuration.TotalMilliseconds:F0} ms | overruns (drop-oldest): {Interlocked.Read(ref _overruns)} | phrase: {_activePhrase?.Name ?? "-"}");
                    break;
            }
        }

        capture.StopRecording();
        output.Stop();
        return 0;
    }

    /// <summary>Single-playback rule: a new trigger replaces the current phrase (decision table).</summary>
    private static void Trigger(float[] data, string name)
    {
        lock (Sync)
        {
            _activePhrase?.Stop();
            _activePhrase = new PhraseSampleProvider(data, name);
            _mixer!.AddMixerInput(_activePhrase);
            if (_duckEnabled)
                _micGain!.SetTarget(RampGain.DbToLinear(_duckDb), DuckRampMs);
            Console.WriteLine($"▶ {name}");
        }
    }

    private static void StopPhrase()
    {
        lock (Sync)
        {
            if (_activePhrase is null) return;
            _activePhrase.Stop(); // 10 ms fade; MixerInputEnded restores the duck
            Console.WriteLine("■ stop");
        }
    }

    private static void ListDevices(MMDeviceEnumerator enumerator)
    {
        Console.WriteLine("Capture devices:");
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            Console.WriteLine($"  {d.FriendlyName}  ({d.AudioClient.MixFormat.SampleRate} Hz)");
        Console.WriteLine("Render devices:");
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            Console.WriteLine($"  {d.FriendlyName}  ({d.AudioClient.MixFormat.SampleRate} Hz)");
    }

    private static MMDevice? FindDevice(MMDeviceEnumerator enumerator, DataFlow flow, string filter) =>
        TryFindDevice(enumerator, flow, filter);

    private static MMDevice? TryFindDevice(MMDeviceEnumerator enumerator, DataFlow flow, string filter) =>
        enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(filter, StringComparison.OrdinalIgnoreCase));

    private static string? ArgValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
