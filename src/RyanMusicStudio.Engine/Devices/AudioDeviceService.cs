using NAudio.CoreAudioApi;

namespace RyanMusicStudio.Engine.Devices;

public sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public IReadOnlyList<AudioDeviceInfo> ListInputs() => List(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceInfo> ListOutputs() => List(DataFlow.Render);

    public MMDevice? GetDevice(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return _enumerator.GetDevice(id); }
        catch { return null; }
    }

    public MMDevice GetDefaultInput() =>
        _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

    public MMDevice GetDefaultOutput() =>
        _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    public AudioDeviceInfo Describe(MMDevice device, bool isInput)
    {
        var mix = device.AudioClient.MixFormat;
        var latencyMs = 0.0;
        try
        {
            latencyMs = device.AudioClient.StreamLatency / 10000.0;
        }
        catch
        {
            latencyMs = 20;
        }

        var name = device.FriendlyName;
        return new AudioDeviceInfo
        {
            Id = device.ID,
            Name = name,
            IsInput = isInput,
            Channels = mix.Channels,
            MixSampleRate = mix.SampleRate,
            BitsPerSample = mix.BitsPerSample,
            LikelyBluetooth = LooksWireless(name),
            ReportedLatencyMs = latencyMs > 0 ? latencyMs : 20
        };
    }

    public static bool LooksWireless(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("bluetooth") || n.Contains("hands-free") || n.Contains("handsfree")
               || n.Contains("airpods") || n.Contains("wireless") || n.Contains("headset (")
               || n.Contains("a2dp") || n.Contains("bt ");
    }

    public static bool LatencyUnsuitable(AudioDeviceInfo device) =>
        device.LikelyBluetooth || device.ReportedLatencyMs >= 80;

    private List<AudioDeviceInfo> List(DataFlow flow)
    {
        var list = new List<AudioDeviceInfo>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            try { list.Add(Describe(device, flow == DataFlow.Capture)); }
            catch { /* skip endpoints that cannot report a mix format */ }
        }
        return list;
    }

    public void Dispose() => _enumerator.Dispose();
}
