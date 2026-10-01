using RyanMusicStudio.Core.Dsp;
using Xunit;
using RyanMusicStudio.Core.Editing;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Engine.IO;

namespace RyanMusicStudio.Tests;

public class PersistenceTests
{
    [Fact]
    public void Save_reopen_preserves_edits_levels_and_effects()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-test-" + Guid.NewGuid().ToString("N"));
        var project = ProjectFactory.CreateVocalOverBeat("Keep Me", root, 92, 3, 4, 44100);
        project.RecordingOffsetFrames = 1234;
        project.Tracks[1].GainDb = -3.5;
        project.Tracks[1].Pan = -0.25;
        VocalPresets.Apply(project.Tracks[1], VocalPresets.Warm);
        project.Markers[0].Lyrics = "hello";
        var store = new ProjectStore();
        store.Save(project);

        var opened = store.Open(root);
        Assert.Equal("Keep Me", opened.Name);
        Assert.Equal(44100, opened.SampleRate);
        Assert.Equal(92, opened.TempoBpm);
        Assert.Equal(3, opened.TimeSignature.Numerator);
        Assert.Equal(1234, opened.RecordingOffsetFrames);
        Assert.Equal(-3.5, opened.Tracks[1].GainDb);
        Assert.Equal(-0.25, opened.Tracks[1].Pan);
        Assert.Contains(opened.Tracks[1].Effects, e => e.Kind == EffectKind.Reverb);
        Assert.Equal("hello", opened.Markers[0].Lyrics);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Atomic_write_keeps_last_valid_file_when_temp_is_abandoned()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rms-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "project.json");
        AtomicFile.WriteAllText(dest, """{"formatVersion":1,"tracks":[]}""");
        File.WriteAllText(dest + ".dead.tmp", "PARTIAL");
        Assert.True(AtomicFile.LooksLikeValidProjectJson(dest));
        Assert.Contains("formatVersion", File.ReadAllText(dest));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Autosave_can_be_recovered_without_replacing_last_good_save()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-as-" + Guid.NewGuid().ToString("N"));
        var store = new ProjectStore();
        var project = ProjectFactory.CreateVocalOverBeat("A", root, 90, 4, 4, 48000);
        store.Save(project);
        project.Name = "B";
        project.Dirty = true;
        store.Autosave(project);
        var good = store.Open(root);
        Assert.Equal("A", good.Name);
        var recovered = store.OpenAutosave(root);
        Assert.Equal("B", recovered.Name);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Incremental_wav_finalizes_before_success_and_repairs_header()
    {
        var path = Path.Combine(Path.GetTempPath(), "rms-wav-" + Guid.NewGuid().ToString("N") + ".wav");
        using (var writer = new IncrementalWavWriter(path, 48000, 1))
        {
            writer.WriteInterleavedFloat(new float[4800]);
            writer.FinalizeHeader();
            Assert.Equal(4800, writer.FramesWritten);
        }
        Assert.True(new FileInfo(path).Length > 44);
        Assert.True(IncrementalWavWriter.RepairHeader(path));
        File.Delete(path);
    }

    [Fact]
    public void Cache_deletion_does_not_break_project_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-cache-" + Guid.NewGuid().ToString("N"));
        var store = new ProjectStore();
        var project = ProjectFactory.CreateVocalOverBeat("C", root, 90, 4, 4, 48000);
        store.Save(project);
        var paths = new ProjectPaths(root);
        Directory.Delete(paths.WaveformCacheDir, true);
        var opened = store.Open(root);
        Assert.Equal("C", opened.Name);
        Directory.Delete(root, true);
    }
}
