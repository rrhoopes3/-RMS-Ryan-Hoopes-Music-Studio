using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Studio;
using Xunit;

namespace RyanMusicStudio.StudioTests;

public class RecordingTrackWorkflowTests
{
    [Fact]
    public void Three_four_pattern_fits_three_beats_and_keeps_all_sixteen_steps()
    {
        var sketch = new SongSketch();
        sketch.Hat[15] = true;
        var commonTime = StudioSynth.Render(sketch, 48000, 120);
        var waltz = StudioSynth.Render(sketch, 48000, 120, 3, 4);
        Assert.Equal(96000, commonTime.Length / 2);
        Assert.Equal(72000, waltz.Length / 2);
        for (var step = 0; step < SongSketch.StepCount; step++)
            Assert.Equal(step, StudioSong.StepAt(48000, 120, step * 72000 / 16, 3, 4));
        Assert.Equal(0, StudioSong.StepAt(48000, 120, 72000, 3, 4));
        Assert.Contains(waltz.Skip(15 * 72000 / 16 * 2), sample => Math.Abs(sample) > 0.001);

        var project = ProjectFactory.CreateStudioSong("Waltz", Path.GetTempPath(), 120);
        project.TimeSignature = new TimeSignature { Numerator = 3, Denominator = 4 };
        StudioSong.Place(project, StudioSong.BarFrames(project));
        Assert.Equal(72000, project.Media.Single(m => m.Id == StudioSong.BedMediaId).LengthFrames);
        Assert.False(StudioSong.NeedsFit(project));
    }

    [Fact]
    public void Six_eight_pattern_uses_denominator_to_fit_three_quarter_beats()
    {
        Assert.Equal(72000, StudioSong.BarFrames(48000, 120, 6, 8));
    }

    [Fact]
    public void Selected_armed_instrument_wins_over_legacy_multiple_armed_tracks()
    {
        var project = ProjectFactory.CreateStudioSong("Song", Path.GetTempPath(), 100);
        var guitar = ProjectFactory.CreateAudioTrack(project, "Guitar", TrackRole.Audio, TrackChannelLayout.Mono);
        guitar.Armed = true;
        Assert.Same(guitar, ProjectFactory.RecordingTarget(project, guitar.Id));
    }

    [Fact]
    public void Selecting_import_lane_keeps_existing_record_target()
    {
        var project = ProjectFactory.CreateStudioSong("Song", Path.GetTempPath(), 100);
        var voice = project.Tracks.Single(t => t.Role == TrackRole.Vocal);
        var backing = project.Tracks.Single(t => t.Role == TrackRole.Backing);
        backing.Armed = true;
        Assert.Same(voice, ProjectFactory.RecordingTarget(project, backing.Id));
    }

    [Fact]
    public void Imported_audio_and_reference_lanes_cannot_capture_over_existing_playback()
    {
        var project = ProjectFactory.CreateStudioSong("Song", Path.GetTempPath(), 100);
        var voice = project.Tracks.Single(t => t.Role == TrackRole.Vocal);
        voice.Clips.Add(new AudioClip());
        var reference = ProjectFactory.CreateAudioTrack(project, "Reference", TrackRole.Reference, TrackChannelLayout.Stereo);
        reference.Armed = true;
        Assert.Null(ProjectFactory.RecordingTarget(project, reference.Id));
    }

    [Fact]
    public void Disarmed_voice_does_not_silently_become_record_target()
    {
        var project = ProjectFactory.CreateStudioSong("Song", Path.GetTempPath(), 100);
        var voice = project.Tracks.Single(t => t.Role == TrackRole.Vocal);
        voice.Armed = false;
        Assert.Null(ProjectFactory.RecordingTarget(project, voice.Id));
    }

    [Fact]
    public void Invalid_tempo_is_rejected_before_a_song_is_created()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectFactory.CreateStudioSong("Song", Path.GetTempPath(), double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectFactory.CreateVocalOverBeat("Song", Path.GetTempPath(), double.NaN, 4, 4, 48000));
    }
}
