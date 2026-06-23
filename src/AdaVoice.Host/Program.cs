using AdaVoice.Audio;
using AdaVoice.Audio.Playback;
using AdaVoice.Audio.Wasapi;
using AdaVoice.Core.Storage;
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

    Console.WriteLine("AdaVoice host.");
    Console.WriteLine($"Mic:   {options.MicName ?? "(default communications)"}");
    Console.WriteLine($"Cable: {options.CableName}");
    Console.WriteLine($"Log:   {logPath}");
    Console.WriteLine();
    Console.WriteLine("Keys: [S] start  [T] stop  [O] OFF AIR  [P] beep  [R] record  [V] preview last  [D] delete last  [E] export  [I] import  [Q] quit");

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
            case ConsoleKey.R: recording = ToggleRecording(host, recording); break;
            case ConsoleKey.V: PreviewLast(host); break;
            case ConsoleKey.D: DeleteLast(host); break;
            case ConsoleKey.E: Export(host); break;
            case ConsoleKey.I: Import(host); break;
            case ConsoleKey.Q: quit = true; break;
        }
    }
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Toggle a recording take. First R: OFF AIR + record. Second R: stop, process, catalogue.
static bool ToggleRecording(EngineHost host, bool recording)
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
        try
        {
            var entry = host.SaveTake(result, $"Take {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Saved {entry.Id}  gainDb={entry.GainDb:F1}  durationMs={entry.DurationMs}");
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

// Preview the most recently catalogued phrase to the monitor (default output, never the cable).
static void PreviewLast(EngineHost host)
{
    var last = host.Phrases.Count > 0 ? host.Phrases[^1] : null;
    if (last is null)
    {
        Console.WriteLine("Nothing to preview.");
        return;
    }

    var error = host.PreviewEntry(last);
    Console.WriteLine(error is null ? $"Previewed {last.Id}" : $"Preview refused: {error}");
}

// Delete the most recently catalogued phrase. The WAV is orphaned (renamed), never destroyed.
static void DeleteLast(EngineHost host)
{
    var last = host.Phrases.Count > 0 ? host.Phrases[^1] : null;
    if (last is null)
    {
        Console.WriteLine("Nothing to delete.");
        return;
    }

    var deleted = host.DeleteEntry(last);
    Console.WriteLine(deleted is null
        ? "Delete failed — not found."
        : $"Deleted {deleted.Id} (orphaned as deleted-{deleted.FileName}).");
}

// Export the whole library (metadata + active phrase WAVs) to a timestamped zip in the data root.
static void Export(EngineHost host)
{
    var path = Path.Combine(AdaVoicePaths.DefaultRoot, $"export-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
    try
    {
        host.ExportLibrary(path);
        Console.WriteLine($"Exported to {path}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Export failed: {ex.Message}");
    }
}

// Import a library archive: prompt for the path and merge/replace, then report what changed.
static void Import(EngineHost host)
{
    Console.Write("Import zip path: ");
    var path = Console.ReadLine()?.Trim().Trim('"');
    if (string.IsNullOrWhiteSpace(path))
    {
        Console.WriteLine("Import cancelled.");
        return;
    }

    Console.Write("Mode — [m]erge or [r]eplace? ");
    var key = Console.ReadKey(intercept: true).Key;
    Console.WriteLine();
    var mode = key == ConsoleKey.R ? ImportMode.Replace : ImportMode.Merge;

    var result = host.ImportLibrary(path, mode);
    Console.WriteLine(result.Success
        ? $"Imported ({mode}): added {result.Added}, skipped {result.Skipped}."
        : $"Import failed: {result.Error}");
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
