using PortAudioSharp;

namespace RyanMusicStudio.Engine.Devices;

public sealed class AudioDeviceService : IDisposable
{
    private readonly object _gate = new();
    private bool _initialized;

    public AudioDeviceService()
    {
        PortAudio.LoadNativeLibrary();
        PortAudio.Initialize();
        _initialized = true;
    }

    public IReadOnlyList<AudioDeviceInfo> ListInputs() => List(true);
    public IReadOnlyList<AudioDeviceInfo> ListOutputs() => List(false);

    public string? DefaultInputId() => IdFor(PortAudio.DefaultInputDevice);
    public string? DefaultOutputId() => IdFor(PortAudio.DefaultOutputDevice);

    public Portable.PortableAudioDevice? GetDevice(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
        {
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                if (IdFor(i) != id) continue;
                var info = PortAudio.GetDeviceInfo(i);
                return new Portable.PortableAudioDevice
                {
                    ID = id, FriendlyName = info.name, Index = i, Info = info
                };
            }
        }
        return null;
    }

    public Portable.PortableAudioDevice? GetDefaultInput() => GetDevice(DefaultInputId());
    public Portable.PortableAudioDevice? GetDefaultOutput() => GetDevice(DefaultOutputId());

    private static string? IdFor(int index)
    {
        if (index < 0 || index >= PortAudio.DeviceCount) return null;
        var info = PortAudio.GetDeviceInfo(index);
        var occurrence = 0;
        for (var i = 0; i < index; i++)
        {
            var other = PortAudio.GetDeviceInfo(i);
            if (other.hostApi == info.hostApi && other.name == info.name) occurrence++;
        }
        return $"pa:{info.hostApi}:{info.name}:{occurrence}";
    }

    private IReadOnlyList<AudioDeviceInfo> List(bool input)
    {
        var result = new List<AudioDeviceInfo>();
        lock (_gate)
        {
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                var channels = input ? info.maxInputChannels : info.maxOutputChannels;
                if (channels <= 0) continue;
                var name = info.name;
                result.Add(new AudioDeviceInfo
                {
                    Id = IdFor(i)!, Name = name, IsInput = input,
                    Channels = channels, MixSampleRate = (int)Math.Round(info.defaultSampleRate),
                    BitsPerSample = 32, LikelyBluetooth = LooksWireless(name),
                    ReportedLatencyMs = 1000 * (input ? info.defaultLowInputLatency : info.defaultLowOutputLatency)
                });
            }
        }
        return result;
    }

    public static bool LooksWireless(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("bluetooth") || n.Contains("airpods") || n.Contains("wireless") ||
               n.Contains("a2dp") || n.Contains("hands-free") || n.Contains("headset (");
    }

    public static bool LatencyUnsuitable(AudioDeviceInfo device) =>
        device.LikelyBluetooth || device.ReportedLatencyMs >= 80;

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_initialized) return;
            _initialized = false;
            PortAudio.Terminate();
        }
    }
}
