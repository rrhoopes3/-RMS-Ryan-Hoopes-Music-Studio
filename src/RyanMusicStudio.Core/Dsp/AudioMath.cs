namespace RyanMusicStudio.Core.Dsp;

public static class AudioMath
{
    public static float DbToLin(double db) => (float)Math.Pow(10.0, db / 20.0);
    public static double LinToDb(float lin) => 20.0 * Math.Log10(Math.Max(1e-12f, Math.Abs(lin)));

    public static void SoftClip(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i];
            buffer[i] = Math.Clamp(x, -2f, 2f);
            if (Math.Abs(x) > 1f)
                buffer[i] = MathF.Tanh(x);
        }
    }

    public static void ApplyEqualPowerPan(float sample, float pan, out float left, out float right)
    {
        var t = (Math.Clamp(pan, -1f, 1f) + 1f) * 0.5f;
        var angle = t * MathF.PI * 0.5f;
        left = sample * MathF.Cos(angle);
        right = sample * MathF.Sin(angle);
    }
}
