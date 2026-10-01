using RyanMusicStudio.Core.Timeline;
using RyanMusicStudio.Engine.IO;

namespace RyanMusicStudio.Engine.Media;

/// <summary>
/// Original synthesized public-domain backing bed for the sample project and tests.
/// </summary>
public static class BackingRenderer
{
    public static CachedAudio RenderBeat(int sampleRate, double tempoBpm, int bars = 16, int numerator = 4, int denominator = 4)
    {
        var bar = TimelineMath.SamplesPerBar(sampleRate, tempoBpm, numerator, denominator);
        var beat = TimelineMath.SamplesPerBeat(sampleRate, tempoBpm);
        var frames = bar * bars;
        var left = new float[frames];
        var right = new float[frames];

        for (var b = 0; b < bars * numerator; b++)
        {
            var pos = b * beat;
            var isDown = b % numerator == 0;
            var isSnare = b % numerator == 2;
            AddDrum(left, right, pos, sampleRate, isDown ? 52f : 0, isDown ? 0.9f : 0, isSnare ? 0.7f : 0);
            AddHat(left, right, pos, sampleRate, 0.18f);
            if (!isDown)
                AddHat(left, right, pos + beat / 2, sampleRate, 0.10f);
        }

        // Simple bass pulse on downbeats
        for (var barIndex = 0; barIndex < bars; barIndex++)
        {
            var root = barIndex % 4 == 3 ? 49f : 55f;
            var start = barIndex * bar;
            AddBass(left, right, start, beat, sampleRate, root, 0.28f);
        }

        var interleaved = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            interleaved[i * 2] = Math.Clamp(left[i], -0.95f, 0.95f);
            interleaved[i * 2 + 1] = Math.Clamp(right[i], -0.95f, 0.95f);
        }

        return new CachedAudio { Interleaved = interleaved, Channels = 2, SampleRate = sampleRate };
    }

    private static void AddDrum(float[] l, float[] r, long pos, int sr, float kickHz, float kickAmp, float snareAmp)
    {
        var kickLen = sr / 8;
        for (var i = 0; i < kickLen && pos + i < l.Length; i++)
        {
            var env = 1f - i / (float)kickLen;
            env *= env;
            if (kickAmp > 0)
            {
                var s = kickAmp * env * MathF.Sin(2 * MathF.PI * kickHz * i / sr);
                l[pos + i] += s;
                r[pos + i] += s * 0.9f;
            }
        }

        var snareLen = sr / 12;
        var rng = new Random(1800 + (int)pos);
        for (var i = 0; i < snareLen && pos + i < l.Length; i++)
        {
            if (snareAmp <= 0) break;
            var env = 1f - i / (float)snareLen;
            var n = (float)(rng.NextDouble() * 2 - 1);
            var s = snareAmp * env * n * 0.55f;
            l[pos + i] += s * 0.85f;
            r[pos + i] += s;
        }
    }

    private static void AddHat(float[] l, float[] r, long pos, int sr, float amp)
    {
        var len = sr / 40;
        var rng = new Random(900 + (int)pos);
        for (var i = 0; i < len && pos + i < l.Length; i++)
        {
            var env = 1f - i / (float)len;
            var n = (float)(rng.NextDouble() * 2 - 1);
            var s = amp * env * n;
            l[pos + i] += s * 0.6f;
            r[pos + i] += s;
        }
    }

    private static void AddBass(float[] l, float[] r, long pos, long len, int sr, float hz, float amp)
    {
        for (var i = 0; i < len && pos + i < l.Length; i++)
        {
            var env = i < 64 ? i / 64f : 1f - i / (float)len;
            env = Math.Clamp(env, 0, 1);
            var s = amp * env * MathF.Sin(2 * MathF.PI * hz * i / sr);
            l[pos + i] += s;
            r[pos + i] += s * 0.92f;
        }
    }
}
