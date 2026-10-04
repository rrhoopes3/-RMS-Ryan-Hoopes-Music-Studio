using NAudio.Wave;
using RyanMusicStudio.Core.Dsp;
using RyanMusicStudio.Core.Editing;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Studio;
using RyanMusicStudio.Engine.IO;
using RyanMusicStudio.Engine.Media;
using RyanMusicStudio.Engine.Mix;
using Xunit;

namespace RyanMusicStudio.Tests;

public sealed class StudioBedMixerTests
{
    [Fact]
    public void LiveBedRepeatsWithoutWrappingOtherAudioOrDoublingTiles()
    {
        var mixer = CreateMixer(export: false);
        var output = new float[24];
        Assert.Equal(output.Length, mixer.Read(output, 0, output.Length));
        Assert.Equal(12, mixer.PlayheadFrames);
        var pattern = new[] { .1f, .2f, .3f, .4f };
        for (var frame = 0; frame < 12; frame++)
        {
            // The ordinary clip starts only once at frame 6, through frame 7.
            var expected = pattern[frame % 4] + (frame is 6 or 7 ? .5f : 0f);
            Assert.Equal(expected, output[frame * 2], 5);
        }
    }

    [Fact]
    public void ExportUsesFiniteTilesWithoutExtendingBed()
    {
        var mixer = CreateMixer(export: true);
        var output = new float[24];
        mixer.Read(output, 0, output.Length);
        Assert.Equal(12, mixer.PlayheadFrames);
        for (var frame = 8; frame < 12; frame++)
            Assert.Equal(0, output[frame * 2]);
        Assert.Equal(.8f, output[12], 5); // one bed tile plus the ordinary clip
    }

    [Fact]
    public void BedPhaseFollowsSeekAndExplicitTransportLoop()
    {
        var mixer = CreateMixer(export: false, loop: new LoopRegion { Enabled = true, StartFrame = 2, EndFrame = 5 });
        mixer.SetPlayhead(4);
        var output = new float[10];
        mixer.Read(output, 0, output.Length);
        Assert.Equal(3, mixer.PlayheadFrames);
        Assert.Equal(new[] { .1f, .3f, .4f, .1f, .3f }, output.Where((_, i) => i % 2 == 0).ToArray());
    }

    [Fact]
    public void BuildDetectsGeneratedBedButLeavesOtherMediaFinite()
    {
        var path = Path.Combine(Path.GetTempPath(), "rms-bed-test-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
                writer.WriteSamples(new[] { .1f, .2f, .3f, .4f }, 0, 4);
            var cache = new SampleCache();
            cache.LoadAbsolute(StudioSong.BedMediaId, path);
            cache.LoadAbsolute("ordinary", path);
            var project = new ProjectDocument();
            StudioSong.Place(project, 4);
            project.Tracks.Add(new Track
            {
                Name = "Imported", Role = TrackRole.Backing,
                Clips = new List<AudioClip> { new() { MediaId = "ordinary", LengthFrames = 4 } }
            });
            var snapshot = ProjectMixer.Build(project, cache, false, false, 0);
            Assert.Single(snapshot.Tracks, t => t.StudioBed != null);
            Assert.Null(snapshot.Tracks.Single(t => t.Name == "Imported").StudioBed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_take_longer_than_one_bar_plays_through_on_a_fresh_studio_song()
    {
        var bedPath = Path.Combine(Path.GetTempPath(), "rms-bed-long-" + Guid.NewGuid().ToString("N") + ".wav");
        var takePath = Path.Combine(Path.GetTempPath(), "rms-take-long-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var writer = new WaveFileWriter(bedPath, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
                writer.WriteSamples(new[] { .1f, .1f, .1f, .1f }, 0, 4);
            using (var writer = new WaveFileWriter(takePath, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
                writer.WriteSamples(Enumerable.Repeat(.5f, 20).ToArray(), 0, 20);

            var project = ProjectFactory.CreateStudioSong("First run",
                Path.Combine(Path.GetTempPath(), "rms-first-" + Guid.NewGuid().ToString("N")), 100, 48000);
            StudioSong.Place(project, 4);
            var take = new Take { StartFrame = 0, LengthFrames = 20 };
            project.Tracks.Single(t => t.Role == TrackRole.Vocal).Takes.Add(take);
            StudioSong.Place(project, 4); // what the bed refresh after a take does
            foreach (var t in project.Tracks)
                t.Effects.Clear();
            project.Master.GainDb = 0;
            project.Master.LimiterEnabled = false;
            Assert.False(project.Loop.Enabled);

            var cache = new SampleCache();
            cache.LoadAbsolute(StudioSong.BedMediaId, bedPath);
            cache.LoadAbsolute(take.Id, takePath);
            var mixer = new ProjectMixer(48000);
            mixer.SetSnapshot(ProjectMixer.Build(project, cache, false, false, 0));
            var output = new float[32];
            mixer.Read(output, 0, output.Length);

            Assert.Equal(16, mixer.PlayheadFrames);
            // Centered, the bed alone is about .07 and bed plus vocal about .42.
            for (var frame = 0; frame < 16; frame++)
                Assert.True(output[frame * 2] > .3f, $"frame {frame} lost the vocal");
        }
        finally
        {
            File.Delete(bedPath);
            File.Delete(takePath);
        }
    }

    [Fact]
    public void Each_track_reports_its_own_peak_and_faders_change_without_a_rebuild()
    {
        var mixer = CreateMixer(export: false);
        var output = new float[24];
        mixer.Read(output, 0, output.Length);
        var meters = mixer.SampleMeters();
        Assert.Equal(.4f, meters.TrackPeaks["Beat"], 5);
        Assert.Equal(.5f, meters.TrackPeaks["Vocal"], 5);
        Assert.Equal(0f, mixer.SampleMeters().TrackPeaks["Vocal"]); // reading resets the peak

        Assert.True(mixer.SetTrackLevels("Vocal", .5f, -1));
        Assert.False(mixer.SetTrackLevels("missing", 1, 0));
        mixer.SetPlayhead(0);
        mixer.Read(output, 0, output.Length);
        Assert.Equal(.3f + .25f, output[6 * 2], 5); // bed step plus the vocal at half gain
        Assert.Equal(.25f, mixer.SampleMeters().TrackPeaks["Vocal"], 5);
    }

    private static ProjectMixer CreateMixer(bool export, LoopRegion? loop = null)
    {
        var bed = new CachedAudio { Interleaved = new[] { .1f, .2f, .3f, .4f }, Channels = 1, SampleRate = 48000 };
        var other = new CachedAudio { Interleaved = new[] { .5f, .5f }, Channels = 1, SampleRate = 48000 };
        var mixer = new ProjectMixer(48000) { ExportMode = export };
        mixer.SetSnapshot(new MixSnapshot
        {
            SampleRate = 48000, TempoBpm = 120, TimeSignature = new TimeSignature(),
            Loop = loop ?? new LoopRegion(), Master = new MasterBus { LimiterEnabled = false, GainDb = 0 },
            Metronome = false, SoftwareMonitor = false, MonitorGain = 0,
            Tracks = new[]
            {
                Track("Beat", bed, new[]
                {
                    new BoundSpan { Audio = bed, Span = new PlaybackSpan(StudioSong.BedMediaId, false, 0, 0, 4, 0, 0) },
                    new BoundSpan { Audio = bed, Span = new PlaybackSpan(StudioSong.BedMediaId, false, 4, 0, 4, 0, 0) }
                }),
                Track("Vocal", null, new[]
                {
                    new BoundSpan { Audio = other, Span = new PlaybackSpan("voice", false, 6, 0, 2, 0, 0) }
                })
            }
        });
        return mixer;
    }

    private static TrackMix Track(string name, CachedAudio? bed, BoundSpan[] spans) => new()
    {
        Id = name, Name = name, Role = TrackRole.Backing, GainLin = 1, Pan = -1, Mute = false, Solo = false,
        Effects = new EffectChainProcessor(), Spans = spans, StudioBed = bed
    };
}
