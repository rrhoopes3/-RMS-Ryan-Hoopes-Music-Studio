namespace RyanMusicStudio.Core.Dsp;

public sealed class Biquad
{
    private float _a1, _a2, _b0, _b1, _b2;
    private float _z1, _z2;

    public void Reset()
    {
        _z1 = 0;
        _z2 = 0;
    }

    public void HighPass(int sampleRate, float cutoffHz, float q = 0.707f)
    {
        var w0 = 2f * MathF.PI * Math.Clamp(cutoffHz, 10f, sampleRate * 0.45f) / sampleRate;
        var cos = MathF.Cos(w0);
        var sin = MathF.Sin(w0);
        var alpha = sin / (2f * Math.Max(0.1f, q));
        var b0 = (1 + cos) * 0.5f;
        var b1 = -(1 + cos);
        var b2 = (1 + cos) * 0.5f;
        var a0 = 1 + alpha;
        var a1 = -2 * cos;
        var a2 = 1 - alpha;
        Set(b0, b1, b2, a0, a1, a2);
    }

    public void Peak(int sampleRate, float freqHz, float q, float gainDb)
    {
        var a = MathF.Pow(10f, gainDb / 40f);
        var w0 = 2f * MathF.PI * Math.Clamp(freqHz, 20f, sampleRate * 0.45f) / sampleRate;
        var cos = MathF.Cos(w0);
        var sin = MathF.Sin(w0);
        var alpha = sin / (2f * Math.Max(0.1f, q));
        var b0 = 1 + alpha * a;
        var b1 = -2 * cos;
        var b2 = 1 - alpha * a;
        var a0 = 1 + alpha / a;
        var a1 = -2 * cos;
        var a2 = 1 - alpha / a;
        Set(b0, b1, b2, a0, a1, a2);
    }

    public void LowShelf(int sampleRate, float freqHz, float gainDb)
    {
        var a = MathF.Pow(10f, gainDb / 40f);
        var w0 = 2f * MathF.PI * Math.Clamp(freqHz, 20f, sampleRate * 0.45f) / sampleRate;
        var cos = MathF.Cos(w0);
        var sin = MathF.Sin(w0);
        var s = 1f;
        var beta = MathF.Sqrt(a) / s;
        var b0 = a * ((a + 1) - (a - 1) * cos + beta * sin);
        var b1 = 2 * a * ((a - 1) - (a + 1) * cos);
        var b2 = a * ((a + 1) - (a - 1) * cos - beta * sin);
        var a0 = (a + 1) + (a - 1) * cos + beta * sin;
        var a1 = -2 * ((a - 1) + (a + 1) * cos);
        var a2 = (a + 1) + (a - 1) * cos - beta * sin;
        Set(b0, b1, b2, a0, a1, a2);
    }

    public void HighShelf(int sampleRate, float freqHz, float gainDb)
    {
        var a = MathF.Pow(10f, gainDb / 40f);
        var w0 = 2f * MathF.PI * Math.Clamp(freqHz, 20f, sampleRate * 0.45f) / sampleRate;
        var cos = MathF.Cos(w0);
        var sin = MathF.Sin(w0);
        var s = 1f;
        var beta = MathF.Sqrt(a) / s;
        var b0 = a * ((a + 1) + (a - 1) * cos + beta * sin);
        var b1 = -2 * a * ((a - 1) + (a + 1) * cos);
        var b2 = a * ((a + 1) + (a - 1) * cos - beta * sin);
        var a0 = (a + 1) - (a - 1) * cos + beta * sin;
        var a1 = 2 * ((a - 1) - (a + 1) * cos);
        var a2 = (a + 1) - (a - 1) * cos - beta * sin;
        Set(b0, b1, b2, a0, a1, a2);
    }

    public void BandPass(int sampleRate, float freqHz, float q)
    {
        var w0 = 2f * MathF.PI * Math.Clamp(freqHz, 20f, sampleRate * 0.45f) / sampleRate;
        var cos = MathF.Cos(w0);
        var sin = MathF.Sin(w0);
        var alpha = sin / (2f * Math.Max(0.1f, q));
        var b0 = alpha;
        var b1 = 0f;
        var b2 = -alpha;
        var a0 = 1 + alpha;
        var a1 = -2 * cos;
        var a2 = 1 - alpha;
        Set(b0, b1, b2, a0, a1, a2);
    }

    public float Process(float x)
    {
        var y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }

    private void Set(float b0, float b1, float b2, float a0, float a1, float a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }
}
