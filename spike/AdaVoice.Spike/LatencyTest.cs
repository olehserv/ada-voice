using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AdaVoice.Spike;

/// <summary>
/// Trigger-to-cable latency self-test: injects a click into the mixer while
/// capturing from CABLE Output, and measures trigger -> first sample above
/// threshold. This includes app buffers AND VB-CABLE's internal buffering —
/// the number the roadmap's "trigger -> cable &lt; 100 ms" gate cares about.
/// (Mouth-to-Chrome still needs the manual loopback recording — see README.)
/// Accuracy is buffer-quantized, roughly ±10 ms.
/// </summary>
public static class LatencyTest
{
    public static void Run(MMDevice cableOutput, Action<PhraseSampleProvider> addToMixer, int rounds = 5)
    {
        Console.WriteLine($"Latency self-test: capturing from '{cableOutput.FriendlyName}', {rounds} clicks...");
        var results = new List<double>();

        for (var round = 0; round < rounds; round++)
        {
            using var capture = new WasapiCapture(cableOutput, true, 20);
            if (capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat &&
                !(capture.WaveFormat.Encoding == WaveFormatEncoding.Extensible && capture.WaveFormat.BitsPerSample == 32))
            {
                Console.WriteLine($"  Unsupported capture format ({capture.WaveFormat}) — skipping self-test.");
                return;
            }

            var detected = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            long triggerTs = 0;
            var channels = capture.WaveFormat.Channels;
            var rate = capture.WaveFormat.SampleRate;

            capture.DataAvailable += (_, e) =>
            {
                if (Interlocked.Read(ref triggerTs) == 0 || detected.Task.IsCompleted) return;
                var arrival = Stopwatch.GetTimestamp();
                var waveBuffer = new WaveBuffer(e.Buffer);
                var totalFrames = e.BytesRecorded / 4 / channels;
                for (var frame = 0; frame < totalFrames; frame++)
                {
                    if (Math.Abs(waveBuffer.FloatBuffer[frame * channels]) > 0.25f)
                    {
                        var arrivalMs = (arrival - Interlocked.Read(ref triggerTs)) * 1000.0 / Stopwatch.Frequency;
                        // The detected sample sat (totalFrames - frame) samples before
                        // the end of this buffer; subtract that transit time.
                        var correction = (totalFrames - frame) * 1000.0 / rate;
                        detected.TrySetResult(arrivalMs - correction);
                        return;
                    }
                }
            };

            capture.StartRecording();
            Thread.Sleep(300); // let the stream settle

            // 50 ms click at -1 dBFS
            var click = new float[2400];
            Array.Fill(click, 0.89f, 0, click.Length);
            Interlocked.Exchange(ref triggerTs, Stopwatch.GetTimestamp());
            addToMixer(new PhraseSampleProvider(click, "latency-click"));

            if (detected.Task.Wait(TimeSpan.FromSeconds(2)))
            {
                results.Add(detected.Task.Result);
                Console.WriteLine($"  round {round + 1}: {detected.Task.Result:F1} ms");
            }
            else
            {
                Console.WriteLine($"  round {round + 1}: click not detected (is the graph running into CABLE Input?)");
            }
            capture.StopRecording();
            Thread.Sleep(200);
        }

        if (results.Count > 0)
        {
            results.Sort();
            var median = results[results.Count / 2];
            Console.WriteLine($"Median trigger -> cable: {median:F1} ms (gate: < 100 ms; app-side target ≈ 40-45 ms + cable buffering)");
        }
    }
}
