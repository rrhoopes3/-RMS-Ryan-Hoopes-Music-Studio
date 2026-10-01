namespace RyanMusicStudio.Engine.IO;

/// <summary>
/// Single-producer / single-consumer ring. No allocations after construction.
/// </summary>
public sealed class FloatRingBuffer
{
    private readonly float[] _buffer;
    private int _write;
    private int _read;
    private int _count;

    public FloatRingBuffer(int capacity)
    {
        _buffer = new float[Math.Max(1024, capacity)];
    }

    public int Capacity => _buffer.Length;
    public int Count => Volatile.Read(ref _count);

    public int Write(ReadOnlySpan<float> source)
    {
        var written = 0;
        var cap = _buffer.Length;
        while (written < source.Length)
        {
            var used = Volatile.Read(ref _count);
            var free = cap - used;
            if (free <= 0) break;
            var chunk = Math.Min(free, Math.Min(source.Length - written, cap - _write));
            source.Slice(written, chunk).CopyTo(_buffer.AsSpan(_write, chunk));
            _write = (_write + chunk) % cap;
            Interlocked.Add(ref _count, chunk);
            written += chunk;
        }
        return written;
    }

    public int Read(Span<float> dest)
    {
        var read = 0;
        var cap = _buffer.Length;
        while (read < dest.Length)
        {
            var used = Volatile.Read(ref _count);
            if (used <= 0) break;
            var chunk = Math.Min(used, Math.Min(dest.Length - read, cap - _read));
            _buffer.AsSpan(_read, chunk).CopyTo(dest.Slice(read, chunk));
            _read = (_read + chunk) % cap;
            Interlocked.Add(ref _count, -chunk);
            read += chunk;
        }
        return read;
    }

    public void Clear()
    {
        _write = 0;
        _read = 0;
        Volatile.Write(ref _count, 0);
    }
}
