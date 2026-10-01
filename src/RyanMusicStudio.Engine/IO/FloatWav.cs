using NAudio.Wave;

namespace RyanMusicStudio.Engine.IO;

public sealed class CachedAudio
{
    public required float[] Interleaved { get; init; }
    public required int Channels { get; init; }
    public required int SampleRate { get; init; }
    public long Frames => Interleaved.Length / Math.Max(1, Channels);
}

public static class FloatWav
{
    public static CachedAudio Load(string path)
    {
        using var reader = new AudioFileReader(path);
        var channels = reader.WaveFormat.Channels;
        var rate = reader.WaveFormat.SampleRate;
        var samples = new List<float>(rate * channels * 8);
        var buf = new float[1024 * channels];
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
                samples.Add(buf[i]);
        }
        return new CachedAudio
        {
            Interleaved = samples.ToArray(),
            Channels = channels,
            SampleRate = rate
        };
    }

    public static void Write(string path, CachedAudio audio)
    {
        using var writer = new IncrementalWavWriter(path, audio.SampleRate, audio.Channels);
        writer.WriteInterleavedFloat(audio.Interleaved);
        writer.FinalizeHeader();
    }
}
