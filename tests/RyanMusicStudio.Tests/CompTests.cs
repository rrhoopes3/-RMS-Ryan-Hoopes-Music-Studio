using RyanMusicStudio.Core.Editing;
using RyanMusicStudio.Core.Model;
using Xunit;

namespace RyanMusicStudio.Tests;

public class CompTests
{
    private const long Seam = 480; // 10 ms at 48 kHz

    private static (Track track, Take a, Take b) TwoTakes()
    {
        var track = new Track { Role = TrackRole.Vocal };
        var a = new Take { Name = "Take 1", StartFrame = 0, LengthFrames = 100_000, RecordedUtc = DateTimeOffset.UtcNow.AddMinutes(-2) };
        var b = new Take { Name = "Take 2", StartFrame = 0, LengthFrames = 100_000, RecordedUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        track.Takes.Add(a);
        track.Takes.Add(b);
        return (track, a, b);
    }

    // Equal-power fades: where two parts overlap, the squared gains add up to 1.
    private static double Power(Track track, long frame) =>
        ClipEditing.ResolvePlayback(track).Sum(s => Math.Pow(s.FadeGain(frame), 2));

    private static IEnumerable<long> Frames(long from, long to) =>
        Enumerable.Range(0, (int)((to - from) / 97)).Select(i => from + i * 97L);

    [Fact]
    public void Choosing_a_part_keeps_the_rest_of_the_heard_take()
    {
        var (track, a, b) = TwoTakes();
        ClipEditing.SeedComp(track, a.Id);
        ClipEditing.ChoosePart(track, b, 30_000, 60_000, Seam);

        var spans = ClipEditing.ResolvePlayback(track);
        Assert.Contains(spans, s => s.SourceId == a.Id && s.TimelineStart == 0);
        Assert.Contains(spans, s => s.SourceId == b.Id && s.TimelineStart == 30_000 && s.TimelineEnd == 60_000);
        Assert.Contains(spans, s => s.SourceId == a.Id && s.TimelineEnd == 100_000);

        // No silence and no dip anywhere inside the song, including both joins.
        foreach (var f in Frames(Seam, 100_000 - Seam))
            Assert.InRange(Power(track, f), 0.99, 1.01);
    }

    [Fact]
    public void Back_to_back_choices_crossfade_instead_of_dipping()
    {
        var (track, a, b) = TwoTakes();
        ClipEditing.ChoosePart(track, a, 0, 50_000, Seam);
        ClipEditing.ChoosePart(track, b, 50_000, 100_000, Seam);

        foreach (var f in Frames(Seam, 100_000 - Seam))
            Assert.InRange(Power(track, f), 0.99, 1.01);
    }

    [Fact]
    public void A_choice_starting_at_an_earlier_join_leaves_no_crossfade_sliver()
    {
        var (track, a, b) = TwoTakes();
        var c = new Take { Name = "Take 3", StartFrame = 0, LengthFrames = 100_000, RecordedUtc = DateTimeOffset.UtcNow };
        track.Takes.Add(c);
        ClipEditing.SeedComp(track, b.Id);
        ClipEditing.ChoosePart(track, a, 10_000, 40_000, Seam);
        ClipEditing.ChoosePart(track, c, 40_000, 70_000, Seam); // starts exactly where a's part ended

        Assert.DoesNotContain(track.Comp.Regions, r => r.TakeId == b.Id && r.LengthFrames <= 2 * Seam);
        foreach (var f in Frames(Seam, 100_000 - Seam))
            Assert.InRange(Power(track, f), 0.99, 1.01);
    }

    [Fact]
    public void Choosing_more_of_the_heard_take_merges_instead_of_crossfading_it_into_itself()
    {
        var (track, a, _) = TwoTakes();
        ClipEditing.SeedComp(track, a.Id);
        ClipEditing.ChoosePart(track, a, 30_000, 60_000, Seam);

        var region = Assert.Single(track.Comp.Regions);
        Assert.Equal(0, region.TimelineStartFrame);
        Assert.Equal(100_000, region.EndFrame);
    }

    [Fact]
    public void A_new_choice_replaces_an_earlier_choice_under_it()
    {
        var (track, a, b) = TwoTakes();
        ClipEditing.ChoosePart(track, a, 40_000, 50_000, Seam);
        ClipEditing.ChoosePart(track, b, 30_000, 60_000, Seam);

        Assert.DoesNotContain(track.Comp.Regions, r => r.TakeId == a.Id);
        Assert.Single(track.Comp.Regions);
    }

    [Fact]
    public void Seed_skips_the_new_punch_take_and_uses_the_one_heard_before()
    {
        var (track, a, b) = TwoTakes();
        var punch = new Take { Name = "Take 3", StartFrame = 20_000, LengthFrames = 10_000, RecordedUtc = DateTimeOffset.UtcNow };
        track.Takes.Add(punch);

        ClipEditing.SeedComp(track, heardTakeId: a.Id, exclude: punch);
        ClipEditing.ChoosePart(track, punch, 20_000, 30_000, Seam);

        Assert.Contains(track.Comp.Regions, r => r.TakeId == a.Id);
        Assert.DoesNotContain(track.Comp.Regions, r => r.TakeId == b.Id);
        foreach (var f in Frames(Seam, 100_000 - Seam))
            Assert.InRange(Power(track, f), 0.99, 1.01);
    }
}
