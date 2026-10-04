namespace RyanMusicStudio.Engine.Audio;

/// <summary>A min/max envelope interval, in project frames relative to the start of the take.</summary>
public readonly record struct RecordingWaveformBucket(long StartFrame, long FrameCount, float Minimum, float Maximum);

/// <summary>A detached snapshot; reading or changing its array cannot affect recording.</summary>
public sealed record RecordingWaveformSnapshot(
    string TrackId, long StartFrame, int SampleRate, long LengthFrames, RecordingWaveformBucket[] Buckets);

/// <summary>
/// Bounded envelope of a whole take. Feed project-rate mono audio from the file writer, never
/// from the capture callback. Adjacent intervals merge when full, retaining every peak.
/// </summary>
public sealed class RecordingWaveform
{
    public const int MaximumCompletedBuckets = 4096;
    private readonly object _gate = new();
    private readonly RecordingWaveformBucket[] _buckets = new RecordingWaveformBucket[MaximumCompletedBuckets];
    private readonly string _trackId;
    private readonly long _startFrame;
    private readonly int _sampleRate;
    private int _count;
    private long _bucketFrames;
    private long _length;
    private long _pendingFrames;
    private float _minimum = float.PositiveInfinity;
    private float _maximum = float.NegativeInfinity;

    public RecordingWaveform(string trackId, long startFrame, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _trackId = trackId;
        _startFrame = Math.Max(0, startFrame);
        _sampleRate = sampleRate;
        _bucketFrames = Math.Max(1, sampleRate / 100); // initially 10 ms per bucket
    }

    public void Append(ReadOnlySpan<float> mono)
    {
        lock (_gate)
        {
            foreach (var sample in mono)
            {
                var value = float.IsFinite(sample) ? sample : 0f;
                _minimum = Math.Min(_minimum, value);
                _maximum = Math.Max(_maximum, value);
                _pendingFrames++;
                _length++;
                if (_pendingFrames != _bucketFrames) continue;
                _buckets[_count++] = new RecordingWaveformBucket(_length - _pendingFrames, _pendingFrames, _minimum, _maximum);
                _pendingFrames = 0;
                _minimum = float.PositiveInfinity;
                _maximum = float.NegativeInfinity;
                if (_count == _buckets.Length) Compact();
            }
        }
    }

    public RecordingWaveformSnapshot Snapshot()
    {
        lock (_gate)
        {
            var buckets = new RecordingWaveformBucket[_count + (_pendingFrames > 0 ? 1 : 0)];
            Array.Copy(_buckets, buckets, _count);
            if (_pendingFrames > 0)
                buckets[^1] = new RecordingWaveformBucket(_length - _pendingFrames, _pendingFrames, _minimum, _maximum);
            return new RecordingWaveformSnapshot(_trackId, _startFrame, _sampleRate, _length, buckets);
        }
    }

    private void Compact()
    {
        for (var i = 0; i < _count / 2; i++)
        {
            var left = _buckets[i * 2];
            var right = _buckets[i * 2 + 1];
            _buckets[i] = new RecordingWaveformBucket(left.StartFrame, left.FrameCount + right.FrameCount,
                Math.Min(left.Minimum, right.Minimum), Math.Max(left.Maximum, right.Maximum));
        }
        _count /= 2;
        _bucketFrames *= 2;
    }
}
