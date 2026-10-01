using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace RyanMusicStudio.Engine.Devices;

public sealed class DeviceWatcher : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private string? _inputId;
    private string? _outputId;
    private bool _registered;

    public event Action<DeviceLostInfo>? DeviceLost;

    /// <summary>Raised on a COM thread when a device is plugged in, removed or changes state.</summary>
    public event Action? DevicesChanged;

    public DeviceWatcher()
    {
        _enumerator.RegisterEndpointNotificationCallback(this);
        _registered = true;
    }

    public void Watch(string? inputId, string? outputId)
    {
        _inputId = inputId;
        _outputId = outputId;
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        if (newState is DeviceState.Active or DeviceState.Disabled)
        {
            if (newState != DeviceState.Active)
                RaiseIfWatched(deviceId, "stopped responding");
        }
        else
        {
            RaiseIfWatched(deviceId, "was unplugged or disabled");
        }
        DevicesChanged?.Invoke();
    }

    public void OnDeviceRemoved(string deviceId)
    {
        RaiseIfWatched(deviceId, "was removed");
        DevicesChanged?.Invoke();
    }

    public void OnDeviceAdded(string pwstrDeviceId) => DevicesChanged?.Invoke();

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => DevicesChanged?.Invoke();

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    private void RaiseIfWatched(string deviceId, string why)
    {
        if (string.Equals(deviceId, _inputId, StringComparison.OrdinalIgnoreCase))
        {
            DeviceLost?.Invoke(new DeviceLostInfo
            {
                DeviceName = "Microphone",
                WasInput = true,
                RecoveryMessage =
                    $"The microphone {why}. Recording stopped. Your finished takes are still saved. Plug the mic back in, choose it again on Audio Setup, then press Record another take."
            });
        }
        else if (string.Equals(deviceId, _outputId, StringComparison.OrdinalIgnoreCase))
        {
            DeviceLost?.Invoke(new DeviceLostInfo
            {
                DeviceName = "Headphones / speakers",
                WasInput = false,
                RecoveryMessage =
                    $"The headphone output {why}. Playback stopped so the song would not jump to a different device. Choose your headphones again on Audio Setup."
            });
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); }
            catch { /* already gone */ }
            _registered = false;
        }
        _enumerator.Dispose();
    }
}
