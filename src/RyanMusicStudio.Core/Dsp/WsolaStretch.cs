namespace RyanMusicStudio.Core.Dsp;

/// <summary>
/// Conservative WSOLA time-stretch for small timing fixes. Does not rewrite the source array.
/// Intended for ratios roughly between 0.90 and 1.12.
/// </summary>
public static class WsolaStretch
{
    public static float[] Stretch(ReadOnlySpan<float> mono, double ratio, int sampleRate)
    {
        ratio = Math.Clamp(ratio, 0.85, 1.18);
        if (Math.Abs(ratio - 1.0) < 0.001)
            return mono.ToArray();

        var window = Math.Max(256, sampleRate / 80);
        var hopAnalysis = window / 2;
        var hopSynthesis = Math.Max(64, (int)Math.Round(hopAnalysis / ratio));
        var search = hopAnalysis / 2;
        var outputLen = (int)Math.Max(window, Math.Round(mono.Length / ratio));
        var output = new float[outputLen];
        var windowFn = Hann(window);

        var read = 0;
        var write = 0;
        while (write + window < output.Length && read + window + search < mono.Length)
        {
            var best = read;
            var bestCorr = float.MinValue;
            var from = Math.Max(0, read - search);
            var to = Math.Min(mono.Length - window, read + search);
            for (var candidate = from; candidate <= to; candidate += 4)
            {
                var corr = 0f;
                var limit = Math.Min(window, output.Length - write);
                for (var i = 0; i < limit; i++)
                    corr += output[write + i] * mono[candidate + i];
                if (corr > bestCorr)
                {
                    bestCorr = corr;
                    best = candidate;
                }
            }

            for (var i = 0; i < window && write + i < output.Length && best + i < mono.Length; i++)
                output[write + i] += mono[best + i] * windowFn[i];

            read += hopAnalysis;
            write += hopSynthesis;
        }

        return output;
    }

    private static float[] Hann(int n)
    {
        var w = new float[n];
        for (var i = 0; i < n; i++)
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (n - 1)));
        return w;
    }
}
