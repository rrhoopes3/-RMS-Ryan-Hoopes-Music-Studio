using RyanMusicStudio.Core.Dsp;
using RyanMusicStudio.Core.Editing;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Engine.IO;
using RyanMusicStudio.Engine.Mix;
using Xunit;

namespace RyanMusicStudio.Tests;

public class MixerRegressionTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void Read_fills_large_exclusive_mode_and_multi_chunk_requests()
    {
        const int sourceFrames = 10_000;
        var source = Enumerable.Range(0, sourceFrames).Select(i => (i + 1) / 20_000f).ToArray();
        var mixer = CreateMixer(source);
        var output = Enumerable.Repeat(-1f, sourceFrames * 2 + 6).ToArray();

        // 3,840 frames is a 48 kHz / 80 ms WASAPI request; 10,000 also crosses
        // multiple internal scratch-buffer boundaries.
        var firstRead = mixer.Read(output, 3, 3_840 * 2);
        var secondRead = mixer.Read(output, 3 + firstRead, (sourceFrames - 3_840) * 2);

        Assert.Equal(3_840 * 2, firstRead);
        Assert.Equal((sourceFrames - 3_840) * 2, secondRead);
        Assert.Equal(sourceFrames, mixer.PlayheadFrames);
        for (var frame = 0; frame < sourceFrames; frame++)
        {
            Assert.Equal(source[frame], output[3 + frame * 2]);
            Assert.Equal(0f, output[3 + frame * 2 + 1]);
        }
        Assert.All(output[..3], sample => Assert.Equal(-1f, sample));
        Assert.All(output[(3 + sourceFrames * 2)..], sample => Assert.Equal(-1f, sample));
    }

    [Fact]
    public void Loop_renders_start_immediately_at_boundary_and_wraps_more_than_once()
    {
        var source = Enumerable.Range(0, 2_000).Select(i => (i + 1) / 10_000f).ToArray();
        var mixer = CreateMixer(source, new LoopRegion { Enabled = true, StartFrame = 100, EndFrame = 1_000 });
        mixer.SetPlayhead(900);
        var output = new float[1_200 * 2];

        Assert.Equal(output.Length, mixer.Read(output, 0, output.Length));
        for (var frame = 0; frame < 1_200; frame++)
        {
            var sourceFrame = frame < 100 ? 900 + frame : 100 + (frame - 100) % 900;
            Assert.Equal(source[sourceFrame], output[frame * 2]);
        }
        Assert.Equal(300, mixer.PlayheadFrames);

        // Seeking beyond a moved loop range should resume at its start.
        mixer.SetPlayhead(1_500);
        var afterSeek = new float[10];
        Assert.Equal(afterSeek.Length, mixer.Read(afterSeek, 0, afterSeek.Length));
        for (var frame = 0; frame < 5; frame++)
            Assert.Equal(source[100 + frame], afterSeek[frame * 2]);
        Assert.Equal(105, mixer.PlayheadFrames);

        var tinyLoop = CreateMixer(source, new LoopRegion { Enabled = true, StartFrame = 10, EndFrame = 13 });
        tinyLoop.SetPlayhead(11);
        var repeated = new float[20 * 2];
        Assert.Equal(repeated.Length, tinyLoop.Read(repeated, 0, repeated.Length));
        for (var frame = 0; frame < 20; frame++)
            Assert.Equal(source[10 + (frame + 1) % 3], repeated[frame * 2]);
        Assert.Equal(10 + (20 + 1) % 3, tinyLoop.PlayheadFrames);
    }

    [Fact]
    public void Large_read_matches_smaller_reads_with_effect_and_monitor_state()
    {
        const int frames = 9_000;
        var source = Enumerable.Range(0, frames).Select(i => (i % 123 + 1) / 2_000f).ToArray();
        var monitorSamples = Enumerable.Range(0, frames).Select(i => (i % 97 + 1) / 4_000f).ToArray();
        var largeMonitor = new FloatRingBuffer(frames);
        var smallMonitor = new FloatRingBuffer(frames);
        Assert.Equal(frames, largeMonitor.Write(monitorSamples));
        Assert.Equal(frames, smallMonitor.Write(monitorSamples));
        var largeMixer = CreateMixer(source, withDelay: true, monitor: largeMonitor);
        var smallMixer = CreateMixer(source, withDelay: true, monitor: smallMonitor);
        var oneRead = new float[frames * 2];
        var smallReads = new float[frames * 2];

        Assert.Equal(oneRead.Length, largeMixer.Read(oneRead, 0, oneRead.Length));
        for (var offset = 0; offset < smallReads.Length; offset += 1_000 * 2)
            Assert.Equal(1_000 * 2, smallMixer.Read(smallReads, offset, 1_000 * 2));

        Assert.Equal(oneRead, smallReads);
        Assert.Equal(frames, largeMixer.PlayheadFrames);
        Assert.Equal(frames, smallMixer.PlayheadFrames);
        Assert.Equal(0, largeMonitor.Count);
        Assert.Equal(0, smallMonitor.Count);
    }

    private static ProjectMixer CreateMixer(
        float[] source, LoopRegion? loop = null, bool withDelay = false, FloatRingBuffer? monitor = null)
    {
        var mixer = new ProjectMixer(SampleRate);
        var effects = new EffectChainProcessor();
        if (withDelay)
            effects.Rebuild([new EffectSlot { Kind = EffectKind.Delay }], SampleRate);
        mixer.SetSnapshot(new MixSnapshot
        {
            SampleRate = SampleRate,
            TempoBpm = 120,
            TimeSignature = new TimeSignature(),
            Loop = loop ?? new LoopRegion(),
            Master = new MasterBus { LimiterEnabled = false },
            Tracks =
            [
                new TrackMix
                {
                    Name = "Test",
                    Role = TrackRole.Audio,
                    GainLin = 1,
                    Pan = -1,
                    Mute = false,
                    Solo = false,
                    Effects = effects,
                    Spans =
                    [
                        new BoundSpan
                        {
                            Span = new PlaybackSpan("source", false, 0, 0, source.Length, 0, 0),
                            Audio = new CachedAudio
                            {
                                Interleaved = source,
                                Channels = 1,
                                SampleRate = SampleRate
                            }
                        }
                    ]
                }
            ],
            Metronome = false,
            SoftwareMonitor = monitor != null,
            MonitorGain = 0.5f
        });
        mixer.SetMonitor(monitor);
        return mixer;
    }
}
