using AdaVoice.Audio;
using AdaVoice.Audio.Playback;
using AdaVoice.Audio.Storage;
using AdaVoice.Audio.Wasapi;
using AdaVoice.Host;
using Serilog;

// Relaunch after a crash: the mic-forwarding process must not stay dead (design 03).
NativeMethods.RegisterApplicationRestart(null, 0);

var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "adavoice-.log");
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var options = new WasapiAudioOptions
    {
        MicName = ArgValue("--mic"),
        CableName = ArgValue("--cable") ?? "CABLE Input",
    };

    using var host = new EngineHost(options, msg =>
    {
        Console.WriteLine(msg);
        Log.Information("{Event}", msg);
    });

    var recordingsDir = Path.Combine(AppContext.BaseDirectory, "recordings");
    Directory.CreateDirectory(recordingsDir);

    Console.WriteLine("AdaVoice host.");
    Console.WriteLine($"Mic:   {options.MicName ?? "(default communications)"}");
    Console.WriteLine($"Cable: {options.CableName}");
    Console.WriteLine($"Log:   {logPath}");
    Console.WriteLine();
    Console.WriteLine("Keys: [S] start  [T] stop  [O] OFF AIR toggle  [P] play beep  [R] record  [Q] quit");

    var offAir = false;
    var recording = false;
    var quit = false;
    while (!quit)
    {
        switch (Console.ReadKey(intercept: true).Key)
        {
            case ConsoleKey.S: host.Start(); break;
            case ConsoleKey.T: host.Stop(); break;
            case ConsoleKey.O:
                offAir = !offAir;
                if (offAir) host.EnterOffAir(); else host.ExitOffAir();
                break;
            case ConsoleKey.P: host.Play(Beep()); break;
            case ConsoleKey.R:
                recording = ToggleRecording(host, recording, recordingsDir);
                break;
            case ConsoleKey.Q: quit = true; break;
        }
    }
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Toggle a recording take. First R: OFF AIR + record. Second R: stop, process, save a WAV.
static bool ToggleRecording(EngineHost host, bool recording, string dir)
{
    if (!recording)
    {
        if (host.TryStartRecording())
        {
            Console.WriteLine("Recording… press R to stop. (OFF AIR)");
            return true;
        }

        Console.WriteLine("Cannot record — press S to go Live first.");
        return false;
    }

    var result = host.StopRecording();
    if (result is { HasSignal: true })
    {
        var path = Path.Combine(dir, $"take-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
        try
        {
            WavFile.Save(path, result.Samples);
            Console.WriteLine($"Saved {path}  gainDb={result.GainDb:F1}  durationMs={result.DurationMs}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Save failed: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("No signal — nothing saved.");
    }

    return false;
}

// A 1-second 660 Hz tone, so [P] sends something audible to the cable.
static Phrase Beep()
{
    var samples = new float[AudioFormats.SampleRate];
    for (var i = 0; i < samples.Length; i++)
        samples[i] = 0.3f * (float)Math.Sin(2 * Math.PI * 660 * i / AudioFormats.SampleRate);
    return new Phrase("beep", samples);
}

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
