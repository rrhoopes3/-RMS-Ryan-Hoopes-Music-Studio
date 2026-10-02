namespace RyanMusicStudio.Engine.Devices;

public sealed class DeviceWatcher : IDisposable
{
    private readonly AudioDeviceService _service = new();
    private readonly Timer _timer;
    private string? _inputId;
    private string? _outputId;
    private string _signature = "";
    private bool _disposed;

    public event Action<DeviceLostInfo>? DeviceLost;
    public event Action? DevicesChanged;

    public DeviceWatcher()
    {
        _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void Watch(string? inputId, string? outputId)
    {
        _inputId = inputId;
        _outputId = outputId;
    }

    private void Poll()
    {
        if (_disposed) return;
        try
        {
            var inputs = _service.ListInputs();
            var outputs = _service.ListOutputs();
            var signature = string.Join('|', inputs.Select(x => x.Id).Concat(outputs.Select(x => x.Id)));
            if (_signature.Length != 0 && signature != _signature)
            {
                if (_inputId != null && inputs.All(x => x.Id != _inputId))
                    DeviceLost?.Invoke(new DeviceLostInfo
                    {
                        DeviceName = "Microphone", WasInput = true,
                        RecoveryMessage = "The selected microphone disappeared. Recording stopped and the take was finalized. Choose a microphone on Audio Setup."
                    });
                if (_outputId != null && outputs.All(x => x.Id != _outputId))
                    DeviceLost?.Invoke(new DeviceLostInfo
                    {
                        DeviceName = "Headphones / speakers", WasInput = false,
                        RecoveryMessage = "The selected output disappeared. Playback stopped. Choose an output on Audio Setup."
                    });
                DevicesChanged?.Invoke();
            }
            _signature = signature;
        }
        catch { /* a transient host-device scan must not crash recording */ }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
        _service.Dispose();
    }
}
