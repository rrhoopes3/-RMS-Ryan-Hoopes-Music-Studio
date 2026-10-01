using RyanMusicStudio.Core.Timeline;
using Xunit;

namespace RyanMusicStudio.Tests;

public class TimelineTests
{
    [Fact]
    public void Positions_are_integer_frames_not_rounded_seconds()
    {
        var frame = TimelineMath.SecondsToFrame(1.5, 48000);
        Assert.Equal(72000, frame);
        Assert.Equal(1.5, TimelineMath.FrameToSeconds(frame, 48000), 9);
    }

    [Fact]
    public void Bar_and_beat_use_time_signature()
    {
        var bar44 = TimelineMath.SamplesPerBar(48000, 120, 4, 4);
        var bar34 = TimelineMath.SamplesPerBar(48000, 120, 3, 4);
        Assert.Equal(4 * TimelineMath.SamplesPerBeat(48000, 120), bar44);
        Assert.Equal(3 * TimelineMath.SamplesPerBeat(48000, 120), bar34);
    }

    [Fact]
    public void Snap_to_beat_stays_on_grid()
    {
        var beat = TimelineMath.SamplesPerBeat(48000, 60);
        var snapped = TimelineMath.Snap(beat + 100, SnapMode.Beat, 48000, 60, 4, 4);
        Assert.Equal(beat, snapped);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void Clock_formats_from_frames(int rate)
    {
        var text = TimelineMath.FormatClock(rate * 65 + rate / 2, rate);
        Assert.StartsWith("01:05", text);
    }
}
