using RyanMusicStudio.Core.Editing;
using Xunit;
using RyanMusicStudio.Core.Model;

namespace RyanMusicStudio.Tests;

public class EditTests
{
    [Fact]
    public void Split_trim_move_slip_fade_delete_do_not_need_audio_files()
    {
        var track = new Track();
        var clip = new AudioClip
        {
            MediaId = "m",
            StartFrame = 1000,
            SourceOffsetFrames = 0,
            LengthFrames = 4000
        };
        track.Clips.Add(clip);

        var right = ClipEditing.Split(track, clip, 2000);
        Assert.NotNull(right);
        Assert.Equal(1000, clip.LengthFrames);
        Assert.Equal(2000, right!.StartFrame);
        Assert.Equal(1000, right.SourceOffsetFrames);
        Assert.Equal(2, track.Clips.Count);

        ClipEditing.Trim(clip, 1200, 1800, 10000);
        Assert.Equal(1200, clip.StartFrame);
        Assert.Equal(200, clip.SourceOffsetFrames);
        Assert.Equal(600, clip.LengthFrames);

        ClipEditing.Move(right, 5000);
        Assert.Equal(5000, right.StartFrame);

        ClipEditing.Slip(right, 50, 10000);
        Assert.Equal(50, right.SourceOffsetFrames);

        ClipEditing.SetFades(right, 100, 100);
        Assert.Equal(100, right.FadeInFrames);
        Assert.Equal(100, right.FadeOutFrames);

        ClipEditing.Crossfade(clip, right, 40);
        Assert.True(right.StartFrame < clip.EndFrame || right.FadeInFrames > 0);

        ClipEditing.Delete(track, right);
        Assert.DoesNotContain(right, track.Clips);
    }

    [Fact]
    public void Comp_regions_play_instead_of_whole_takes()
    {
        var track = new Track();
        var take = new Take { StartFrame = 0, LengthFrames = 8000 };
        track.Takes.Add(take);
        ClipEditing.AddCompRegion(track, take, 100, 0, 500);
        var spans = ClipEditing.ResolvePlayback(track);
        Assert.Single(spans);
        Assert.Equal(100, spans[0].TimelineStart);
        Assert.Equal(500, spans[0].Length);
        Assert.True(spans[0].FromTake);
    }

    [Fact]
    public void Undo_restores_clip_metadata()
    {
        var project = ProjectFactory.CreateVocalOverBeat("t", Path.GetTempPath(), 90, 4, 4, 48000);
        var track = project.Tracks[0];
        track.Clips.Add(new AudioClip { StartFrame = 0, LengthFrames = 1000, MediaId = "x" });
        var undo = new UndoStack();
        undo.RememberBeforeChange(project);
        track.Clips[0].StartFrame = 999;
        var restored = undo.Undo(project);
        Assert.NotNull(restored);
        Assert.Equal(0, restored!.Tracks[0].Clips[0].StartFrame);
    }
}
