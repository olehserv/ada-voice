namespace AdaVoice.Audio.Dsp;

/// <summary>
/// Removes leading and trailing silence from a recorded take, keeping a short padding so speech is
/// not clipped (design 06 §3: threshold −45 dBFS, keep 150 ms). An all-silent take returns empty,
/// which the recorder treats as "no signal".
/// </summary>
public static class SilenceTrim
{
    public static float[] Trim(float[] samples, int sampleRate, double thresholdDbfs = -45, int paddingMs = 150)
    {
        var threshold = RampGain.DbToLinear(thresholdDbfs);

        int first = -1, last = -1;
        for (var i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) >= threshold)
            {
                if (first < 0)
                    first = i;
                last = i;
            }
        }

        if (first < 0)
            return []; // all silent

        var pad = paddingMs * sampleRate / 1000;
        var start = Math.Max(0, first - pad);
        var end = Math.Min(samples.Length, last + 1 + pad);
        return samples[start..end];
    }
}
