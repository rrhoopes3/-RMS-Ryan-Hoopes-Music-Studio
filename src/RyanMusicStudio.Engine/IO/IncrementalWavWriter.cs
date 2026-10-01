using System.Buffers.Binary;
using System.Text;

namespace RyanMusicStudio.Engine.IO;

/// <summary>
/// Writes IEEE-float WAV incrementally. Finalize patches the header before callers
/// may report success. A crash leaves a file that <see cref="RepairHeader"/> can finish.
/// </summary>
public sealed class IncrementalWavWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly byte[] _header = new byte[44];
    private readonly object _gate = new();
    private long _dataBytes;
    private bool _finalized;

    public string Path { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public long FramesWritten => _dataBytes / (4L * Channels);

    public IncrementalWavWriter(string path, int sampleRate, int channels)
    {
        Path = path;
        SampleRate = sampleRate;
        Channels = channels;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        WriteHeaderPlaceholder();
    }

    public void WriteInterleavedFloat(ReadOnlySpan<float> samples)
    {
        if (_finalized) throw new InvalidOperationException("Writer already finalized.");
        if (samples.IsEmpty) return;
        Span<byte> bytes = stackalloc byte[Math.Min(samples.Length, 256) * 4];
        var offset = 0;
        lock (_gate)
        {
            while (offset < samples.Length)
            {
                var n = Math.Min(samples.Length - offset, bytes.Length / 4);
                for (var i = 0; i < n; i++)
                    BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(i * 4, 4), samples[offset + i]);
                _stream.Write(bytes[..(n * 4)]);
                _dataBytes += n * 4L;
                offset += n;
            }
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            _stream.Flush(true);
        }
    }

    public void FinalizeHeader()
    {
        lock (_gate)
        {
            if (_finalized) return;
            _stream.Flush(true);
            Patch(_stream, _dataBytes, SampleRate, Channels);
            _stream.Flush(true);
            _finalized = true;
        }
    }

    public void Dispose()
    {
        if (!_finalized)
        {
            try { FinalizeHeader(); }
            catch { /* best effort */ }
        }
        _stream.Dispose();
    }

    public static bool RepairHeader(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length < 44) return false;
        var dataBytes = stream.Length - 44;
        var header = new byte[44];
        stream.Position = 0;
        stream.ReadExactly(header);
        var channels = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(22, 2));
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24, 4));
        if (channels <= 0 || sampleRate <= 0) return false;
        Patch(stream, dataBytes, sampleRate, channels);
        stream.Flush(true);
        return dataBytes > 0;
    }

    private void WriteHeaderPlaceholder()
    {
        Patch(_stream, 0, SampleRate, Channels);
        _stream.Flush(true);
    }

    private static void Patch(FileStream stream, long dataBytes, int sampleRate, int channels)
    {
        var header = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), (int)Math.Min(int.MaxValue, 36 + dataBytes));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20), 3); // IEEE float
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), sampleRate);
        var byteRate = sampleRate * channels * 4;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32), (short)(channels * 4));
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34), 32);
        Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), (int)Math.Min(int.MaxValue, dataBytes));
        var pos = stream.Position;
        stream.Position = 0;
        stream.Write(header);
        stream.Position = Math.Max(44, pos);
    }
}
