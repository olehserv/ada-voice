using AdaVoice.Audio;
using AdaVoice.Audio.Dsp;
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

if (args.Contains("--list"))
{
    PrintDevices();
    return 0;
}

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
