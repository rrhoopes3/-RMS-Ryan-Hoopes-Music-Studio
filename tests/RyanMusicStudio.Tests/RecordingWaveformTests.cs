using RyanMusicStudio.Engine.Audio;
using Xunit;

namespace RyanMusicStudio.Tests;

public sealed class RecordingWaveformTests
{
    [Fact]
    public void PartialBucketsKeepFramePositionsAcrossWrites()
    {
        var waveform = new RecordingWaveform("voice", 1234, 1000);
        waveform.Append(new float[] { -.8f, .2f, .5f });
        waveform.Append(new float[] { -.2f, .9f });
        var snapshot = waveform.Snapshot();
        Assert.Equal("voice", snapshot.TrackId);
        Assert.Equal(1234, snapshot.StartFrame);
        Assert.Equal(1000, snapshot.SampleRate);
        Assert.Equal(5, snapshot.LengthFrames);
        Assert.Equal(new RecordingWaveformBucket(0, 5, -.8f, .9f), Assert.Single(snapshot.Buckets));
        snapshot.Buckets[0] = default;
        Assert.Equal(-.8f, waveform.Snapshot().Buckets[0].Minimum);
    }

    [Fact]
    public void LongTakesStayBoundedAndRetainWholeTakePeaks()
    {
        var waveform = new RecordingWaveform("voice", 0, 100);
        var samples = new float[100_003];
        samples[0] = -.97f;
        samples[50_000] = .83f;
        samples[^1] = -.4f;
        // Different chunk boundaries must not lose partial buckets.
        for (var offset = 0; offset < samples.Length; offset += 137)
            waveform.Append(samples.AsSpan(offset, Math.Min(137, samples.Length - offset)));
        var snapshot = waveform.Snapshot();
        Assert.Equal(samples.Length, snapshot.LengthFrames);
        Assert.InRange(snapshot.Buckets.Length, 1, RecordingWaveform.MaximumCompletedBuckets + 1);
        Assert.Equal(-.97f, snapshot.Buckets.Min(b => b.Minimum));
        Assert.Equal(.83f, snapshot.Buckets.Max(b => b.Maximum));
        long next = 0;
        foreach (var bucket in snapshot.Buckets)
        {
            Assert.Equal(next, bucket.StartFrame);
            next += bucket.FrameCount;
        }
        Assert.Equal(samples.Length, next);
    }

    [Fact]
    public async Task SnapshotsAreConsistentWhileWriterAppends()
    {
        var waveform = new RecordingWaveform("voice", 0, 48000);
        var writer = Task.Run(() =>
        {
            var samples = Enumerable.Repeat(.25f, 512).ToArray();
            for (var i = 0; i < 1000; i++) waveform.Append(samples);
        });
        for (var i = 0; i < 100; i++)
        {
            var snapshot = waveform.Snapshot();
            Assert.Equal(snapshot.LengthFrames, snapshot.Buckets.Sum(b => b.FrameCount));
            Assert.All(snapshot.Buckets, bucket => Assert.Equal(.25f, bucket.Maximum));
        }
        await writer;
        Assert.Equal(512_000, waveform.Snapshot().LengthFrames);
    }

    [Fact]
    public void NewTakeStartsEmptyAndNonFiniteInputDoesNotPoisonEnvelope()
    {
        var previous = new RecordingWaveform("voice", 0, 1000);
        previous.Append(new float[] { 1f });
        var next = new RecordingWaveform("voice", 5000, 1000);
        Assert.Empty(next.Snapshot().Buckets);
        next.Append(new float[] { float.NaN, float.PositiveInfinity, -.5f });
        Assert.Equal(new RecordingWaveformBucket(0, 3, -.5f, 0), Assert.Single(next.Snapshot().Buckets));
        Assert.Equal(1, previous.Snapshot().LengthFrames);
    }
}
