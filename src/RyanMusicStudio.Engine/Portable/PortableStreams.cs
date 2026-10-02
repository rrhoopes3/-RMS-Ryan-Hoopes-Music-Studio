using System.Runtime.InteropServices;
using NAudio.Wave;
using PortAudioSharp;
using PaStream = PortAudioSharp.Stream;

namespace RyanMusicStudio.Engine.Portable;

public sealed class PortableAudioCapture : IDisposable
{
    private readonly PortableAudioDevice _device;
    private readonly int _bufferMs;
    private PaStream? _stream;
    private byte[] _bytes = new byte[65536];
    private bool _running;

    public WaveFormat WaveFormat { get; }
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<PortableAudioStoppedEventArgs>? RecordingStopped;

    public PortableAudioCapture(PortableAudioDevice device, bool exclusive, int bufferMilliseconds)
    {
        _device = device;
        _bufferMs = bufferMilliseconds;
        var channels = Math.Min(2, device.Info.maxInputChannels);
        if (channels < 1) throw new InvalidOperationException("This device cannot capture audio.");
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            (int)Math.Round(device.Info.defaultSampleRate), channels);
    }

    public void StartRecording()
    {
        if (_running) return;
        var p = new StreamParameters
        {
            device = _device.Index, channelCount = WaveFormat.Channels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = Math.Max(_device.Info.defaultLowInputLatency, _bufferMs / 1000.0),
            hostApiSpecificStreamInfo = IntPtr.Zero
        };
        _stream = new PaStream(p, null, WaveFormat.SampleRate,
            PortAudio.FramesPerBufferUnspecified, StreamFlags.NoFlag, Capture, this);
        try { _stream.Start(); _running = true; }
        catch { _stream.Dispose(); _stream = null; throw; }
    }

    private StreamCallbackResult Capture(IntPtr input, IntPtr output, uint frames,
        ref StreamCallbackTimeInfo time, StreamCallbackFlags flags, IntPtr userData)
    {
        if (input == IntPtr.Zero) return StreamCallbackResult.Continue;
        var count = checked((int)frames * WaveFormat.Channels * sizeof(float));
        if (count > _bytes.Length) _bytes = new byte[count];
        Marshal.Copy(input, _bytes, 0, count);
        try { DataAvailable?.Invoke(this, new WaveInEventArgs(_bytes, count)); }
        catch (Exception ex)
        {
            _running = false;
            Task.Run(() => RecordingStopped?.Invoke(this, new PortableAudioStoppedEventArgs(ex)));
            return StreamCallbackResult.Abort;
        }
        return StreamCallbackResult.Continue;
    }

    public void StopRecording()
    {
        if (!_running) return;
        _running = false;
        _stream?.Stop();
        RecordingStopped?.Invoke(this, new PortableAudioStoppedEventArgs(null));
    }

    public void Dispose()
    {
        StopRecording();
        _stream?.Dispose();
        _stream = null;
    }
}

public sealed class PortableAudioOutput : IDisposable
{
    private readonly PortableAudioDevice _device;
    private readonly int _bufferMs;
    private PaStream? _stream;
    private ISampleProvider? _provider;
    private float[] _samples = new float[32768];
    private bool _stopping;

    public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
    public event EventHandler<PortableAudioStoppedEventArgs>? PlaybackStopped;

    public PortableAudioOutput(PortableAudioDevice device, PortableShareMode share,
        bool eventSync, int bufferMilliseconds)
    {
        _device = device;
        _bufferMs = bufferMilliseconds;
    }

    public void Init(ISampleProvider provider)
    {
        if (_stream != null) throw new InvalidOperationException("Output already initialized.");
        if (_device.Info.maxOutputChannels < provider.WaveFormat.Channels)
            throw new InvalidOperationException("The selected output has too few channels.");
        _provider = provider;
        var p = new StreamParameters
        {
            device = _device.Index, channelCount = provider.WaveFormat.Channels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = Math.Max(_device.Info.defaultLowOutputLatency, _bufferMs / 1000.0),
            hostApiSpecificStreamInfo = IntPtr.Zero
        };
        _stream = new PaStream(null, p, provider.WaveFormat.SampleRate,
            PortAudio.FramesPerBufferUnspecified, StreamFlags.NoFlag, Render, this);
    }

    private StreamCallbackResult Render(IntPtr input, IntPtr output, uint frames,
        ref StreamCallbackTimeInfo time, StreamCallbackFlags flags, IntPtr userData)
    {
        var provider = _provider;
        if (provider == null || output == IntPtr.Zero) return StreamCallbackResult.Abort;
        var count = checked((int)frames * provider.WaveFormat.Channels);
        if (count > _samples.Length) _samples = new float[count];
        try
        {
            var read = provider.Read(_samples, 0, count);
            if (read < count) Array.Clear(_samples, read, count - read);
            Marshal.Copy(_samples, 0, output, count);
            if (read > 0) return StreamCallbackResult.Continue;
            PlaybackState = PlaybackState.Stopped;
            Task.Run(() => PlaybackStopped?.Invoke(this, new PortableAudioStoppedEventArgs(null)));
            return StreamCallbackResult.Complete;
        }
        catch (Exception ex)
        {
            Array.Clear(_samples, 0, count);
            Marshal.Copy(_samples, 0, output, count);
            PlaybackState = PlaybackState.Stopped;
            Task.Run(() => PlaybackStopped?.Invoke(this, new PortableAudioStoppedEventArgs(ex)));
            return StreamCallbackResult.Abort;
        }
    }

    public void Play()
    {
        if (_stream == null) throw new InvalidOperationException("Output was not initialized.");
        if (PlaybackState == PlaybackState.Playing) return;
        _stream.Start();
        PlaybackState = PlaybackState.Playing;
    }

    public void Stop()
    {
        if (_stopping || _stream == null) return;
        _stopping = true;
        try
        {
            if (PlaybackState == PlaybackState.Playing) _stream.Stop();
            PlaybackState = PlaybackState.Stopped;
        }
        finally { _stopping = false; }
    }

    public void Dispose()
    {
        Stop();
        _stream?.Dispose();
        _stream = null;
    }
}
