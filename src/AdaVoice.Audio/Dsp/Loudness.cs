namespace AdaVoice.Audio.Dsp;

/// <summary>
/// Level measurement on a block of float samples: linear RMS (average loudness) and linear peak
/// (largest absolute sample). The recorder uses these to loudness-match a take and to keep it under
/// a peak ceiling. Values are linear (0..1+); convert to dBFS with <c>20·log10(value)</c>.
/// </summary>
public static class Loudness
{
    /// <summary>Root-mean-square level of the samples. 0 for an empty buffer.</summary>
    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
            return 0;

        double sum = 0;
        foreach (var s in samples)
            sum += (double)s * s;

        return Math.Sqrt(sum / samples.Length);
    }

    /// <summary>Largest absolute sample value. 0 for an empty buffer.</summary>
    public static double Peak(ReadOnlySpan<float> samples)
    {
        double peak = 0;
        foreach (var s in samples)
        {
            var a = Math.Abs((double)s);
            if (a > peak)
                peak = a;
        }

        return peak;
    }
}
