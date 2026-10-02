using NAudio.Wave;
using PortAudioSharp;
using RyanMusicStudio.Engine.Devices;

namespace RyanMusicStudio.Engine.Portable;

// The WASAPI engine's transport is reused on macOS and Linux. These small
// adapters give it the same device information while PortAudio owns the I/O.
public enum PortableShareMode { Shared, Exclusive }

public sealed class PortableAudioDevice : IDisposable
{
    public required string ID { get; init; }
    public required string FriendlyName { get; init; }
    public required int Index { get; init; }
    public required DeviceInfo Info { get; init; }
    public PortableAudioClient AudioClient => new(Info);
    public void Dispose() { }
}

public sealed class PortableAudioClient(DeviceInfo info)
{
    public WaveFormat MixFormat => WaveFormat.CreateIeeeFloatWaveFormat(
        (int)Math.Round(info.defaultSampleRate), Math.Max(1, info.maxInputChannels));
}

public sealed class PortableAudioStoppedEventArgs(Exception? exception) : EventArgs
{
    public Exception? Exception { get; } = exception;
}
