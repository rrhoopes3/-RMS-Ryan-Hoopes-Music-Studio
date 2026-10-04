using RyanMusicStudio.Core.Model;

namespace RyanMusicStudio.Core.Studio;

/// <summary>
/// Renders the beginner sketch to stereo float audio the existing mixer can play.
/// Drums and piano notes are synthesized here. No samples are borrowed from elsewhere.
/// </summary>
public static class StudioSynth
{
    private static readonly float[] NoteHz = [261.63f, 293.66f, 329.63f, 349.23f, 392.00f, 440.00f, 493.88f, 523.25f];

    public static float[] Render(SongSketch sketch, int sampleRate, double tempoBpm)
    {
        sketch.Normalize();
        var frames = StudioSong.BarFrames(sampleRate, tempoBpm);
        var buffer = new float[frames * 2];
        var step = Math.Max(1, frames / SongSketch.StepCount);
        // Volume lives on the master fader so the wav stays at full level.
        const float gain = 1f;

        for (var i = 0; i < SongSketch.StepCount; i++)
        {
            var at = (int)(i * step);
            if (sketch.Kick[i]) AddDrum(buffer, sampleRate, at, DrumVoice.Kick, gain, wrap: true);
            if (sketch.Snare[i]) AddDrum(buffer, sampleRate, at, DrumVoice.Snare, gain, wrap: true);
            if (sketch.Hat[i]) AddDrum(buffer, sampleRate, at, DrumVoice.Hat, gain, wrap: true);
            if (sketch.Melody[i] >= 0)
                AddPiano(buffer, sampleRate, at, sketch.Melody[i], gain, wrap: true);
        }

        return buffer;
    }

    public static float[] PreviewDrum(SongSketch sketch, DrumVoice voice, int sampleRate)
    {
        var frames = Math.Max(1, sampleRate / 5);
        var buffer = new float[frames * 2];
        AddDrum(buffer, sampleRate, 0, voice, sketch.VolumeGain, wrap: false);
        return buffer;
    }

    public static float[] PreviewNote(SongSketch sketch, int degree, int sampleRate)
    {
        var frames = Math.Max(1, (int)(sampleRate * 0.45));
        var buffer = new float[frames * 2];
        AddPiano(buffer, sampleRate, 0, degree, sketch.VolumeGain, wrap: false);
        return buffer;
    }

    public static void AddDrum(float[] stereo, int sampleRate, int start, DrumVoice voice, float gain, bool wrap = false)
    {
        switch (voice)
        {
            case DrumVoice.Kick:
                AddKick(stereo, sampleRate, start, gain, wrap);
                break;
            case DrumVoice.Snare:
                AddSnare(stereo, sampleRate, start, gain, wrap);
                break;
            default:
                AddHat(stereo, sampleRate, start, gain, wrap);
                break;
        }
    }

    public static void AddPiano(float[] stereo, int sampleRate, int start, int degree, float gain, bool wrap = false)
    {
        if (degree < 0 || degree >= NoteHz.Length) return;
        var hz = NoteHz[degree];
        var length = (int)(sampleRate * 0.55);
        var hammer = new Random(1700 + degree * 17 + start);
        for (var i = 0; i < length; i++)
        {
            var at = start + i;
            var t = i / (double)sampleRate;
            var body = Math.Exp(-t * 3.2);
            var sample = 0.0;
            for (var partial = 1; partial <= 5; partial++)
            {
                var stretch = 1 + 0.00015 * partial * partial;
                var amp = partial switch { 1 => 1.0, 2 => 0.38, 3 => 0.18, 4 => 0.08, _ => 0.04 };
                var decay = Math.Exp(-t * (2.4 + partial * 1.6));
                sample += amp * decay * Math.Sin(2 * Math.PI * hz * partial * stretch * t);
            }
            var knock = (hammer.NextDouble() * 2 - 1) * Math.Exp(-t * 180) * 0.18;
            var s = (float)((sample * 0.22 * body + knock) * gain);
            Mix(stereo, at, s, s * 0.96f, wrap);
        }
    }

    private static void AddKick(float[] stereo, int sampleRate, int start, float gain, bool wrap)
    {
        var length = sampleRate / 5;
        double phase = 0;
        for (var i = 0; i < length; i++)
        {
            var at = start + i;
            var t = i / (double)sampleRate;
            var env = Math.Exp(-t * 14);
            var freq = 42 + 150 * Math.Exp(-t * 22);
            phase += freq / sampleRate;
            var body = Math.Sin(2 * Math.PI * phase);
            var click = Math.Exp(-t * 90) * Math.Sin(2 * Math.PI * 900 * t);
            var s = (float)((body * 0.72 * env + click * 0.18) * gain);
            Mix(stereo, at, s, s, wrap);
        }
    }

    private static void AddSnare(float[] stereo, int sampleRate, int start, float gain, bool wrap)
    {
        var length = sampleRate / 8;
        var noise = new Random(900 + start);
        double phase = 0;
        for (var i = 0; i < length; i++)
        {
            var at = start + i;
            var t = i / (double)sampleRate;
            var env = Math.Exp(-t * 16);
            phase += 196.0 / sampleRate;
            var tone = Math.Sin(2 * Math.PI * phase) * Math.Exp(-t * 28);
            var n = noise.NextDouble() * 2 - 1;
            var s = (float)((tone * 0.28 + n * 0.42) * env * gain);
            Mix(stereo, at, s * 0.92f, s, wrap);
        }
    }

    private static void AddHat(float[] stereo, int sampleRate, int start, float gain, bool wrap)
    {
        var length = sampleRate / 18;
        var noise = new Random(400 + start);
        double previous = 0;
        for (var i = 0; i < length; i++)
        {
            var at = start + i;
            var t = i / (double)sampleRate;
            var env = Math.Exp(-t * 48);
            var n = noise.NextDouble() * 2 - 1;
            var high = n - previous;
            previous = n;
            var s = (float)(high * 0.28 * env * gain);
            Mix(stereo, at, s * 0.75f, s, wrap);
        }
    }

    private static void Mix(float[] stereo, int frame, float left, float right, bool wrap)
    {
        var frames = stereo.Length / 2;
        if (frames <= 0) return;
        if (frame < 0 || frame >= frames)
        {
            if (!wrap) return;
            frame %= frames;
            if (frame < 0) frame += frames;
        }
        var i = frame * 2;
        stereo[i] += left;
        stereo[i + 1] += right;
    }
}
