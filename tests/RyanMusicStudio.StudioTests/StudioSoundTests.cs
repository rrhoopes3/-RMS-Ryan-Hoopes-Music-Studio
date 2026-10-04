using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Core.Studio;
using RyanMusicStudio.Core.Timeline;
using Xunit;

namespace RyanMusicStudio.StudioTests;

public class StudioSoundTests
{
    [Fact]
    public void Empty_song_is_silent()
    {
        var audio = StudioSynth.Render(new SongSketch(), 48000, 100);
        Assert.True(Peak(audio) < 0.00001);
    }

    [Fact]
    public void Kick_hits_only_where_it_is_turned_on()
    {
        var sketch = new SongSketch { VolumePercent = 100 };
        sketch.Kick[0] = true;
        var audio = StudioSynth.Render(sketch, 48000, 100);
        var step = StepFrames(48000, 100);
        Assert.True(Rms(audio, 0, step) > 0.05);
        Assert.True(Rms(audio, step * 8, step) < 0.001);
    }

    [Fact]
    public void Snare_and_hat_make_sound()
    {
        var snare = new SongSketch { VolumePercent = 100 };
        snare.Snare[0] = true;
        var hat = new SongSketch { VolumePercent = 100 };
        hat.Hat[0] = true;
        Assert.True(Rms(StudioSynth.Render(snare, 48000, 100), 0, 2000) > 0.02);
        Assert.True(Rms(StudioSynth.Render(hat, 48000, 100), 0, 1500) > 0.01);
    }

    [Fact]
    public void Piano_note_makes_sound_on_its_step()
    {
        var sketch = new SongSketch { VolumePercent = 100 };
        sketch.Melody[0] = 0;
        var audio = StudioSynth.Render(sketch, 48000, 100);
        var step = StepFrames(48000, 100);
        Assert.True(Rms(audio, 0, step) > 0.02);
        Assert.True(Rms(audio, step * 12, step) < 0.001);
    }

    [Fact]
    public void Volume_changes_the_level()
    {
        var loud = new SongSketch { VolumePercent = 100 };
        loud.Kick[0] = true;
        var mid = new SongSketch { VolumePercent = 50 };
        mid.Kick[0] = true;
        var mute = new SongSketch { VolumePercent = 0 };
        mute.Kick[0] = true;

        var step = StepFrames(48000, 100);
        var loudRms = Rms(StudioSynth.Render(loud, 48000, 100), 0, step);
        var midRms = Rms(StudioSynth.Render(mid, 48000, 100), 0, step);
        Assert.True(loudRms > 0.05);
        Assert.InRange(loudRms / midRms, 1.8, 2.2);
        Assert.True(Peak(StudioSynth.Render(mute, 48000, 100)) < 0.00001);
    }

    [Fact]
    public void Save_and_reopen_keeps_the_beat_melody_tempo_and_volume()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-studio-" + Guid.NewGuid().ToString("N"));
        var project = ProjectFactory.CreateStudioSong("Hello", root, 104, 48000);
        project.Studio.Kick[0] = true;
        project.Studio.Kick[8] = true;
        project.Studio.Snare[4] = true;
        project.Studio.Hat[2] = true;
        project.Studio.Melody[0] = 4;
        project.Studio.VolumePercent = 60;
        var audio = StudioSynth.Render(project.Studio, project.SampleRate, project.TempoBpm);
        StudioSong.Place(project, audio.Length / 2);

        var store = new ProjectStore();
        store.Save(project);
        var opened = store.Open(root);

        Assert.Equal(104, opened.TempoBpm);
        Assert.Equal(60, opened.Studio.VolumePercent);
        Assert.True(opened.Studio.Kick[0]);
        Assert.True(opened.Studio.Kick[8]);
        Assert.False(opened.Studio.Kick[1]);
        Assert.True(opened.Studio.Snare[4]);
        Assert.True(opened.Studio.Hat[2]);
        Assert.Equal(4, opened.Studio.Melody[0]);
        Assert.Equal(-1, opened.Studio.Melody[1]);
        Assert.Equal(StudioSong.BedMediaId, opened.Media.Single().Id);
        Assert.True(opened.Loop.Enabled);
        Assert.Equal(audio.Length / 2, opened.Loop.EndFrame);
        Assert.Equal("Voice", opened.Tracks.Single(t => t.Role == TrackRole.Vocal).Name);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Older_project_files_still_open_without_a_sketch()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-old-" + Guid.NewGuid().ToString("N"));
        var project = ProjectFactory.CreateVocalOverBeat("Old", root, 90, 4, 4, 48000);
        var store = new ProjectStore();
        store.Save(project);
        var json = File.ReadAllText(Path.Combine(root, "project.json"));
        Assert.Contains("\"studio\"", json);

        var stripped = System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"studio\"\\s*:\\s*\\{[\\s\\S]*?\\}\\s*\\n", "\n");
        File.WriteAllText(Path.Combine(root, "project.json"), stripped);
        Assert.DoesNotContain("\"studio\"", File.ReadAllText(Path.Combine(root, "project.json")));
        var opened = store.Open(root);
        Assert.True(opened.Studio.IsEmpty);
        Assert.Equal("Old", opened.Name);
        Directory.Delete(root, true);
    }

    private static int StepFrames(int sampleRate, double tempo) =>
        Math.Max(1, (int)(TimelineMath.SamplesPerBeat(sampleRate, tempo) / 4));

    private static double Rms(float[] stereo, int startFrame, int frames)
    {
        double sum = 0;
        var count = 0;
        var end = Math.Min(stereo.Length / 2, startFrame + frames);
        for (var i = Math.Max(0, startFrame); i < end; i++)
        {
            var left = stereo[i * 2];
            var right = stereo[i * 2 + 1];
            sum += left * left + right * right;
            count += 2;
        }
        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    private static float Peak(float[] stereo)
    {
        var peak = 0f;
        foreach (var sample in stereo)
            peak = Math.Max(peak, Math.Abs(sample));
        return peak;
    }
}
