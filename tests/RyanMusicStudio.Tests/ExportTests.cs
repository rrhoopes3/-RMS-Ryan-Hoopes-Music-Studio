using NAudio.Wave;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Engine.Export;
using RyanMusicStudio.Engine.IO;
using RyanMusicStudio.Engine.Media;
using Xunit;

namespace RyanMusicStudio.Tests;

public sealed class ExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rms-export-" + Guid.NewGuid().ToString("N"));
    private const int Rate = 48000;
    private const int Frames = 20000;

    private ProjectDocument Song()
    {
        var project = ProjectFactory.CreateVocalOverBeat("Export test", _root, 90, 4, 4, Rate);
        new ProjectPaths(_root).EnsureLayout();
        var media = new AudioMedia { WorkingRelativePath = "media/working/source.wav", LengthFrames = Frames };
        FloatWav.Write(Path.Combine(_root, media.WorkingRelativePath), new CachedAudio
        {
            SampleRate = Rate, Channels = 1,
            Interleaved = Enumerable.Range(0, Frames).Select(i => 0.2f * MathF.Sin(i * 0.03f)).ToArray()
        });
        project.Media.Add(media);
        project.Tracks[0].Clips.Add(new AudioClip { MediaId = media.Id, LengthFrames = Frames });
        project.Loop = new LoopRegion { Enabled = true, StartFrame = 100, EndFrame = 500 };
        return project;
    }

    [Theory]
    [InlineData(ExportFormat.Wav16, 16)]
    [InlineData(ExportFormat.Wav24, 24)]
    public void Export_writes_exact_selected_range_across_chunks_and_ignores_loop(ExportFormat format, int bits)
    {
        var project = Song();
        var dest = Path.Combine(_root, "song.wav");
        var progress = new List<double>();
        new MixExporter().Export(project, new SampleCache(), dest, format, ExportScope.SelectedRange,
            1234, Frames - 57, progress: new InlineProgress(progress.Add));

        using var reader = new WaveFileReader(dest);
        Assert.Equal(bits, reader.WaveFormat.BitsPerSample);
        Assert.Equal(2, reader.WaveFormat.Channels);
        Assert.Equal(Rate, reader.WaveFormat.SampleRate);
        Assert.Equal((Frames - 57 - 1234) * 2 * (bits / 8), reader.Length);
        var audio = FloatWav.Load(dest);
        Assert.Contains(audio.Interleaved, sample => Math.Abs(sample) > 0.05f);
        // A loop leak would repeat the opening samples every 400 frames.
        Assert.True(Math.Abs(audio.Interleaved[1000] - audio.Interleaved[1800]) > 0.001f);
        Assert.Equal(1, progress[^1]);
        Assert.Equal(progress.OrderBy(p => p), progress);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp.*"));
    }

    [Fact]
    public void Cancelling_mid_render_preserves_existing_export_and_removes_temporary_audio()
    {
        var project = Song();
        var dest = Path.Combine(_root, "song.wav");
        File.WriteAllText(dest, "previous export");
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new MixExporter().Export(project, new SampleCache(),
            dest, ExportFormat.Wav16, ExportScope.WholeProject, 0, Frames, cancellation.Token,
            new InlineProgress(_ => cancellation.Cancel())));
        Assert.Equal("previous export", File.ReadAllText(dest));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp.*"));
    }

    [Fact]
    public void Failed_final_replace_removes_temporary_audio()
    {
        var project = Song();
        var dest = Path.Combine(_root, "song.wav");
        Directory.CreateDirectory(dest);
        var error = Record.Exception(() => new MixExporter().Export(project, new SampleCache(), dest,
            ExportFormat.Wav24, ExportScope.WholeProject, 0, Frames));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.True(Directory.Exists(dest));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp.*"));
    }

    [Fact]
    public void Export_cannot_overwrite_source_recordings()
    {
        var project = Song();
        var source = Path.Combine(_root, project.Media[0].WorkingRelativePath);
        var original = File.ReadAllBytes(source);
        Assert.Throws<IOException>(() => new MixExporter().Export(project, new SampleCache(), source,
            ExportFormat.Wav16, ExportScope.WholeProject, 0, Frames));
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Fact]
    public void Empty_range_does_not_replace_a_previous_export()
    {
        var project = Song();
        var dest = Path.Combine(_root, "song.wav");
        File.WriteAllText(dest, "previous export");
        Assert.Throws<InvalidOperationException>(() => new MixExporter().Export(project, new SampleCache(), dest,
            ExportFormat.Wav16, ExportScope.SelectedRange, 100, 100));
        Assert.Equal("previous export", File.ReadAllText(dest));
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
