namespace RyanMusicStudio.Engine.IO;

/// <summary>
/// Mono linear resampler that carries its phase and last sample across blocks, so a 48 kHz mic in a
/// 44.1 kHz song loses no frames at block edges (takes stay in time, monitoring doesn't crackle).
/// One instance per thread; no allocations.
/// </summary>
public sealed class StreamResampler
{
    private double _pos;
    private float _prev;

    public void Reset()
    {
        _pos = 0;
        _prev = 0;
    }

    /// <param name="ratio">Input rate divided by output rate.</param>
    /// <param name="dst">Must hold at least <c>src.Length / ratio + 2</c> samples.</param>
    public int Process(ReadOnlySpan<float> src, double ratio, Span<float> dst)
    {
        if (src.Length == 0) return 0;
        var o = 0;
        while (_pos < src.Length - 1 && o < dst.Length)
        {
            var i0 = (int)Math.Floor(_pos); // -1 means the previous block's last sample
            var a = i0 < 0 ? _prev : src[i0];
            var b = src[i0 + 1];
            dst[o++] = a + (b - a) * (float)(_pos - i0);
            _pos += ratio;
        }
        _pos -= src.Length;
        _prev = src[^1];
        return o;
    }
}
