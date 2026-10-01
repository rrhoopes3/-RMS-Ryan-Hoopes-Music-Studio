namespace RyanMusicStudio.Engine.Devices;

public sealed class AudioDeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsInput { get; init; }
    public int Channels { get; init; }
    public int MixSampleRate { get; init; }
    public int BitsPerSample { get; init; }
    public bool LikelyBluetooth { get; init; }
    public double ReportedLatencyMs { get; init; }

    public string ChannelLabel => Channels <= 1 ? "1 channel (mono)" : $"{Channels} channels";

    public string DetailLine =>
        $"{ChannelLabel} · {MixSampleRate:N0} Hz · ~{ReportedLatencyMs:0} ms";
}

public sealed class DeviceLostInfo
{
    public required string DeviceName { get; init; }
    public required bool WasInput { get; init; }
    public required string RecoveryMessage { get; init; }
}
