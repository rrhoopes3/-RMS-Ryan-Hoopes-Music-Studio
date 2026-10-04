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
        Name = name, Role = TrackRole.Backing, GainLin = 1, Pan = -1, Mute = false, Solo = false,
        Effects = new EffectChainProcessor(), Spans = spans, StudioBed = bed
    };
}
