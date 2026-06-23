using AdaVoice.Audio;
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Passthrough;
using AdaVoice.Audio.Wasapi;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;

// Phase 1 step 4 — validate the production WASAPI seams on real hardware.
// This wires the REAL capture and render seams through the SAME MicPassthrough the
// engine will use. If the provisional render seam (Init(ISampleProvider) -> WasapiOut)
// holds here, the engine can safely be built on top of it.
//
// Usage:
//   dotnet run --project tools/AudioSeamCheck -- --list
//   dotnet run --project tools/AudioSeamCheck                 (default comms mic -> CABLE Input)
//   dotnet run --project tools/AudioSeamCheck -- --mic "USB"  (pick mic by name)
//   dotnet run --project tools/AudioSeamCheck -- --factory    (same passthrough, via WasapiDeviceFactory)
//   dotnet run --project tools/AudioSeamCheck -- --monitor    (print device add/remove/default events)

if (args.Contains("--list"))
{
    PrintDevices();
    return 0;
}

if (args.Contains("--monitor"))
    return RunMonitor();

if (args.Contains("--factory"))
    return RunFactory();

var micName = ArgValue("--mic");
var cableName = ArgValue("--cable") ?? "CABLE Input";

var mic = micName is null
    ? WasapiDevices.DefaultCommunicationsMic()
    : WasapiDevices.FindByName(DataFlow.Capture, micName);
var cable = WasapiDevices.FindByName(DataFlow.Render, cableName);

if (mic is null)
{
    Console.Error.WriteLine($"Mic not found: '{micName}'. Run with --list.");
    return 1;
}

if (cable is null)
{
    Console.Error.WriteLine($"Render device '{cableName}' not found. Is VB-CABLE installed?");
    return 1;
}

var cableFormat = cable.AudioClient.MixFormat;
Console.WriteLine($"Mic:   {mic.FriendlyName}");
Console.WriteLine($"Cable: {cable.FriendlyName} ({cableFormat.SampleRate} Hz, {cableFormat.Channels} ch)");
if (cableFormat.SampleRate != AudioFormats.SampleRate)
    Console.WriteLine("  WARNING: cable is not at 48 kHz. Set both CABLE endpoints to 48 kHz in Sound settings.");

using var capture = new WasapiCaptureDevice(mic);
using var passthrough = new MicPassthrough(capture);
using var render = new WasapiRenderDevice(cable);

var mixer = new MixingSampleProvider(AudioFormats.Engine) { ReadFully = true };
mixer.AddMixerInput(passthrough.Output);

render.Init(mixer);
render.Start();
capture.Start();

Console.WriteLine(render.DuckingOptOutError is { } err
    ? $"WARNING: ducking opt-out failed ({err.Message}). Fallback: Sound > Communications > Do nothing."
    : "Ducking opt-out applied to the cable session.");

Console.WriteLine();
Console.WriteLine("LIVE — passthrough running through the PRODUCTION seam.");
Console.WriteLine("In Chrome pick 'CABLE Output' as the mic, or use Windows 'Listen to this device' on CABLE Output.");
Console.WriteLine("Keys: [D] duck on/off   [I] info   [Q] quit");

var ducked = false;
while (true)
{
    var key = Console.ReadKey(intercept: true);
    if (key.Key == ConsoleKey.Q)
        break;

    switch (key.Key)
    {
        case ConsoleKey.D:
            ducked = !ducked;
            passthrough.Duck(ducked ? RampGain.DbToLinear(-12) : 1f, rampMs: 50);
            Console.WriteLine($"Duck: {(ducked ? "ON (-12 dB)" : "OFF")}");
            break;
        case ConsoleKey.I:
            Console.WriteLine(
                $"mic gain: {passthrough.CurrentMicGain:F3} | overruns: {passthrough.Overruns} | render: {render.State}");
            break;
    }
}

capture.Stop();
render.Stop();
return 0;

void PrintDevices()
{
    Console.WriteLine("Capture devices:");
    foreach (var d in WasapiDevices.Active(DataFlow.Capture))
        Console.WriteLine($"  {d.FriendlyName}");
    Console.WriteLine("Render devices:");
    foreach (var d in WasapiDevices.Active(DataFlow.Render))
        Console.WriteLine($"  {d.FriendlyName}");
}

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// Validate WasapiDeviceFactory on real hardware: resolve all three roles through it and run the
// same mic -> CABLE passthrough, now built entirely via the factory.
int RunFactory()
{
    var options = new WasapiAudioOptions
    {
        MicName = ArgValue("--mic"),
        CableName = ArgValue("--cable") ?? "CABLE Input",
    };
    var factory = new WasapiDeviceFactory(options);

    using var capture = factory.CreateCapture(DeviceRole.Mic);
    using var cableRender = factory.CreateRender(DeviceRole.Cable);

    // Confirm the alarm device resolves too (system default output). We do not sound it here.
    using (var alarm = factory.CreateRender(DeviceRole.Alarm))
        Console.WriteLine($"Alarm device resolved: {alarm.Format.SampleRate} Hz, {alarm.Format.Channels} ch.");

    using var passthrough = new MicPassthrough(capture);
    var mixer = new MixingSampleProvider(AudioFormats.Engine) { ReadFully = true };
    mixer.AddMixerInput(passthrough.Output);

    cableRender.Init(mixer);
    cableRender.Start();
    capture.Start();

    Console.WriteLine($"Cable: {cableRender.Format.SampleRate} Hz, {cableRender.Format.Channels} ch.");
    Console.WriteLine();
    Console.WriteLine("LIVE via WasapiDeviceFactory — mic -> CABLE.");
    Console.WriteLine("Keys: [D] duck on/off   [Q] quit");

    var ducked = false;
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Q)
            break;
        if (key.Key == ConsoleKey.D)
        {
            ducked = !ducked;
            passthrough.Duck(ducked ? RampGain.DbToLinear(-12) : 1f, rampMs: 50);
            Console.WriteLine($"Duck: {(ducked ? "ON (-12 dB)" : "OFF")}");
        }
    }

    capture.Stop();
    cableRender.Stop();
    return 0;
}

// Validate WasapiDeviceMonitor on real hardware: print each device change as it arrives.
int RunMonitor()
{
    using var monitor = new WasapiDeviceMonitor();
    monitor.DeviceChanged += (_, e) => Console.WriteLine($"{e.Kind,-14} {e.DeviceId}");
    monitor.Start();

    Console.WriteLine("Listening for device changes.");
    Console.WriteLine("Unplug/replug a device, or change the default output, to see events.");
    Console.WriteLine("Press Q to stop.");
    while (Console.ReadKey(intercept: true).Key != ConsoleKey.Q) { }

    monitor.Stop();
    return 0;
}
