using System.Buffers.Binary;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Engine.IO;

namespace RyanMusicStudio.Engine.Media;

public sealed class PeakData
{
    public required float[] Min { get; init; }
    public required float[] Max { get; init; }
    public required int Hop { get; init; }
}

public sealed class WaveformCache
{
    public const int DefaultHop = 256;

    public PeakData Build(string audioPath, string cachePath, int hop = DefaultHop)
    {
        var audio = FloatWav.Load(audioPath);
        var frames = audio.Frames;
        var buckets = (int)Math.Ceiling(frames / (double)hop);
        var min = new float[buckets];
        var max = new float[buckets];
        for (var i = 0; i < buckets; i++)
        {
            var start = (long)i * hop;
            var end = Math.Min(frames, start + hop);
            var lo = 1f;
            var hi = -1f;
            for (var f = start; f < end; f++)
            {
                for (var c = 0; c < audio.Channels; c++)
                {
                    var s = audio.Interleaved[f * audio.Channels + c];
                    if (s < lo) lo = s;
                    if (s > hi) hi = s;
                }
            }
            min[i] = lo;
            max[i] = hi;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        using var stream = File.Create(cachePath);
        Span<byte> hdr = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(hdr, 0x31504B52); // RPK1
        BinaryPrimitives.WriteInt32LittleEndian(hdr[4..], hop);
        BinaryPrimitives.WriteInt32LittleEndian(hdr[8..], buckets);
        BinaryPrimitives.WriteInt32LittleEndian(hdr[12..], audio.SampleRate);
        stream.Write(hdr);
        var payload = new byte[buckets * 8];
        for (var i = 0; i < buckets; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(i * 8, 4), min[i]);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(i * 8 + 4, 4), max[i]);
        }
        stream.Write(payload);

        return new PeakData { Min = min, Max = max, Hop = hop };
    }

    public PeakData LoadOrBuild(ProjectPaths paths, string audioRelativeOrAbsolute, string cacheKey)
    {
        var audioPath = Path.IsPathRooted(audioRelativeOrAbsolute)
            ? audioRelativeOrAbsolute
            : Path.Combine(paths.Root, audioRelativeOrAbsolute.Replace('/', Path.DirectorySeparatorChar));
        var cachePath = Path.Combine(paths.WaveformCacheDir, cacheKey + ".rpk");
        if (File.Exists(cachePath) && File.Exists(audioPath) &&
            File.GetLastWriteTimeUtc(cachePath) >= File.GetLastWriteTimeUtc(audioPath))
        {
            try { return Load(cachePath); }
            catch { /* rebuild */ }
        }
        return Build(audioPath, cachePath);
    }

    public static PeakData Load(string cachePath)
    {
        var bytes = File.ReadAllBytes(cachePath);
        if (bytes.Length < 16) throw new InvalidDataException("Waveform cache is too small.");
        var hop = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        var buckets = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
        var min = new float[buckets];
        var max = new float[buckets];
        for (var i = 0; i < buckets; i++)
        {
            min[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(16 + i * 8, 4));
            max[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(16 + i * 8 + 4, 4));
        }
        return new PeakData { Min = min, Max = max, Hop = hop };
    }
}
