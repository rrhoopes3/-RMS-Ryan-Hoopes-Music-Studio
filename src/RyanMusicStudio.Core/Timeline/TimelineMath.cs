namespace RyanMusicStudio.Core.Timeline;

public enum SnapMode
{
    Off,
    Bar,
    Beat,
    Eighth,
    Frame
}

public static class TimelineMath
{
    public static long SamplesPerBeat(int sampleRate, double tempoBpm)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (tempoBpm <= 0) throw new ArgumentOutOfRangeException(nameof(tempoBpm));
        return (long)Math.Round(sampleRate * 60.0 / tempoBpm);
    }

    public static long SamplesPerBar(int sampleRate, double tempoBpm, int numerator, int denominator)
    {
        if (numerator <= 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0 || (denominator & (denominator - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(denominator), "Time-signature denominator must be a power of two.");
        var quarter = SamplesPerBeat(sampleRate, tempoBpm);
        // Beat unit is a quarter-note at the given tempo (standard DAW mapping).
        var beatsPerBar = numerator * (4.0 / denominator);
        return (long)Math.Round(quarter * beatsPerBar);
    }

    public static long SecondsToFrame(double seconds, int sampleRate)
    {
        if (seconds < 0) seconds = 0;
        return (long)Math.Round(seconds * sampleRate);
    }

    public static double FrameToSeconds(long frame, int sampleRate)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        return frame / (double)sampleRate;
    }

    public static (int Bar, int Beat, long FrameInBeat) FrameToBarBeat(
        long frame, int sampleRate, double tempoBpm, int numerator, int denominator)
    {
        var barLen = SamplesPerBar(sampleRate, tempoBpm, numerator, denominator);
        var beatLen = Math.Max(1, barLen / Math.Max(1, numerator));
        if (frame < 0) frame = 0;
        var bar = (int)(frame / barLen);
        var rem = frame % barLen;
        var beat = (int)(rem / beatLen);
        var frameInBeat = rem % beatLen;
        return (bar + 1, beat + 1, frameInBeat);
    }

    public static string FormatClock(long frame, int sampleRate)
    {
        var total = Math.Max(0, frame) / (double)sampleRate;
        var minutes = (int)(total / 60);
        var seconds = total - minutes * 60;
        return $"{minutes:00}:{seconds:00.000}";
    }

    public static long Snap(long frame, SnapMode mode, int sampleRate, double tempoBpm, int numerator, int denominator)
    {
        if (mode == SnapMode.Off || mode == SnapMode.Frame || frame <= 0)
            return Math.Max(0, frame);

        var bar = SamplesPerBar(sampleRate, tempoBpm, numerator, denominator);
        var beat = SamplesPerBeat(sampleRate, tempoBpm);
        var grid = mode switch
        {
            SnapMode.Bar => bar,
            SnapMode.Beat => beat,
            SnapMode.Eighth => Math.Max(1, beat / 2),
            _ => 1
        };
        return (long)Math.Round(frame / (double)grid) * grid;
    }

    public static long ClampNonNegative(long frame) => frame < 0 ? 0 : frame;

    public static long OverlapLength(long aStart, long aLen, long bStart, long bLen)
    {
        var aEnd = aStart + aLen;
        var bEnd = bStart + bLen;
        var start = Math.Max(aStart, bStart);
        var end = Math.Min(aEnd, bEnd);
        return Math.Max(0, end - start);
    }
}
