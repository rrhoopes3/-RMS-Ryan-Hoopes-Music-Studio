using NAudio.CoreAudioApi;
using NAudio.Wave;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Core.Timeline;
using RyanMusicStudio.Engine.Devices;
using RyanMusicStudio.Engine.IO;
using RyanMusicStudio.Engine.Media;
using RyanMusicStudio.Engine.Mix;

namespace RyanMusicStudio.Engine.Audio;

public sealed class EngineConfig
{
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public bool ExclusiveMode { get; set; }
    public bool SoftwareMonitor { get; set; } = true;
    public int BufferMilliseconds { get; set; } = 20;
    public long UserOffsetFrames { get; set; }
}

public sealed class RecordedTakeResult
{
    public required Take Take { get; init; }
    public required string AbsolutePath { get; init; }
}

public sealed class AudioEngine : IDisposable
{
    private readonly AudioDeviceService _devices = new();
    private readonly DeviceWatcher _watcher = new();
    private readonly SampleCache _cache = new();
    private readonly MediaImporter _importer = new();
    private readonly ProjectStore _store = new();
    private readonly object _gate = new();
    private readonly FloatRingBuffer _captureRing = new(48000 * 8);
    private readonly FloatRingBuffer _monitorRing = new(48000 * 4);
    private readonly float[] _captureConvert = new float[16384];

    private EngineConfig _config = new();
    private ProjectDocument? _project;
    private ProjectMixer? _mixer;
    private WasapiOut? _output;
    private WasapiCapture? _capture;
    private IncrementalWavWriter? _writer;
    private CancellationTokenSource? _writerCts;
    private Task? _writerTask;
    private MMDevice? _inputDevice;
    private MMDevice? _outputDevice;
    private int _captureChannels = 1;
    private int _captureRate = 48000;
    private volatile bool _recording;
    private volatile bool _playing;
    private volatile bool _countIn;
    private long _recordStartPlayhead;
    private long _countInRemaining;
    private string? _inProgressTakePath;
    private Track? _armedTrack;
    private int _takeSerial;
    private bool _loopPassPending;

    public event Action<string>? StatusChanged;
    public event Action<DeviceLostInfo>? DeviceLost;
    public event Action<MeterState>? Meters;
    public event Action<RecordedTakeResult>? TakeCommitted;
    public event Action<long>? PlayheadMoved;

    public AudioDeviceService Devices => _devices;
    public SampleCache Cache => _cache;
    public MediaImporter Importer => _importer;
    public bool IsRecording => _recording;
    public bool IsPlaying => _playing;
    public bool SoftwareMonitor
    {
        get => _config.SoftwareMonitor;
        set
        {
            _config.SoftwareMonitor = value;
            RebuildMix();
        }
    }

    public bool MetronomeEnabled { get; set; } = true;
    public long PlayheadFrames => _mixer?.PlayheadFrames ?? 0;
    public EngineConfig Config => _config;

    public long ReportedCompensationFrames { get; private set; }

    public AudioEngine()
    {
        _watcher.DeviceLost += OnDeviceLost;
    }

    public IReadOnlyList<AudioDeviceInfo> Inputs() => _devices.ListInputs();
    public IReadOnlyList<AudioDeviceInfo> Outputs() => _devices.ListOutputs();

    public void Configure(EngineConfig config)
    {
        StopTransport(safeFinalize: true);
        _config = config;
        _inputDevice?.Dispose();
        _outputDevice?.Dispose();
        _inputDevice = ResolveInput(config.InputDeviceId);
        _outputDevice = ResolveOutput(config.OutputDeviceId);
        _watcher.Watch(_inputDevice?.ID, _outputDevice?.ID);
        UpdateCompensation();
        Status($"Using {_inputDevice?.FriendlyName ?? "no mic"} in, {_outputDevice?.FriendlyName ?? "no speakers"} out.");
    }

    public void AttachProject(ProjectDocument project)
    {
        _project = project;
        _cache.PreloadProject(project);
        EnsureMixer(project.SampleRate);
        RebuildMix();
        _mixer!.SetPlayhead(0);
    }

    public void NotifyProjectChanged() => RebuildMix();

    public void SetPlayhead(long frame) => _mixer?.SetPlayhead(frame);

    public void Play()
    {
        if (_project == null) throw new InvalidOperationException("Open a project first.");
        StartOutput();
        StartInputMeter();
        _playing = true;
        Status("Playing");
    }

    public void Stop()
    {
        if (_recording)
            _ = StopRecordAsync();
        else
            StopTransport(safeFinalize: false);
        Status("Stopped");
    }

    public void StartRecord()
    {
        if (_project == null) throw new InvalidOperationException("Open a project first.");
        _armedTrack = _project.Tracks.FirstOrDefault(t => t.Armed) ??
                      _project.Tracks.FirstOrDefault(t => t.Role == TrackRole.Vocal);
        if (_armedTrack == null)
            throw new InvalidOperationException("Arm a vocal track before recording.");

        StartOutput();
        StartInputMeter();
        var bar = TimelineMath.SamplesPerBar(_project.SampleRate, _project.TempoBpm,
            _project.TimeSignature.Numerator, _project.TimeSignature.Denominator);
        var playhead = _mixer!.PlayheadFrames;
        if (_project.Punch.Enabled)
            playhead = Math.Max(0, _project.Punch.StartFrame - _project.PreRollBars * bar);
        else if (_project.CountInBars > 0)
            playhead = Math.Max(0, playhead);

        _mixer.SetPlayhead(playhead);
        _countInRemaining = _project.CountInBars * bar;
        _countIn = _countInRemaining > 0;
        _recordStartPlayhead = _project.Punch.Enabled ? _project.Punch.StartFrame : playhead + _countInRemaining;
        _playing = true;
        if (!_countIn)
            BeginTakeFile();
        Status(_countIn ? "Count-in…" : "Recording");
    }

    public async Task<RecordedTakeResult?> StopRecordAsync()
    {
        RecordedTakeResult? result = null;
        if (_recording || _writer != null)
            result = await FinalizeTakeAsync().ConfigureAwait(false);
        StopTransport(safeFinalize: false);
        return result;
    }

    public async Task<string> RecordTestTakeAsync(TimeSpan duration, string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"test-take-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
        var rate = _inputDevice?.AudioClient.MixFormat.SampleRate ?? 48000;
        var channels = 1;
        StartInputMeter();
        var writer = new IncrementalWavWriter(path, rate, channels);
        var end = DateTime.UtcNow + duration;
        var buf = new float[2048];
        while (DateTime.UtcNow < end)
        {
            var n = _captureRing.Read(buf);
            if (n > 0)
            {
                if (_captureChannels > 1)
                {
                    var mono = new float[n / _captureChannels];
                    for (var i = 0; i < mono.Length; i++)
                    {
                        var acc = 0f;
                        for (var c = 0; c < _captureChannels; c++)
                            acc += buf[i * _captureChannels + c];
                        mono[i] = acc / _captureChannels;
                    }
                    writer.WriteInterleavedFloat(mono);
                }
                else
                {
                    writer.WriteInterleavedFloat(buf.AsSpan(0, n));
                }
            }
            else
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
        writer.FinalizeHeader();
        writer.Dispose();
        Status("Test take saved. Playing it back…");
        return path;
    }

    public void PlayFile(string path)
    {
        StopTransport(false);
        var audio = _cache.LoadAbsolute(path, path);
        var provider = new CachedAudioProvider(audio);
        OpenOutput(provider);
        _output!.Play();
        _playing = true;
    }

    public void PlayTestTone()
    {
        StopTransport(false);
        var rate = _outputDevice?.AudioClient.MixFormat.SampleRate ?? 48000;
        OpenOutput(new TestToneProvider(rate, 2.2));
        _output!.Play();
        _playing = true;
        Status("Playing a test tone in your headphones.");
    }

    public AudioMedia ImportIntoProject(string filePath, Track? track)
    {
        if (_project == null) throw new InvalidOperationException("Open a project first.");
        var media = _importer.Import(_project, filePath, track);
        var abs = Path.Combine(_project.RootPath, media.WorkingRelativePath.Replace('/', Path.DirectorySeparatorChar));
        _cache.LoadAbsolute(media.Id, abs);
        RebuildMix();
        return media;
    }

    public void TickUi()
    {
        if (_mixer == null) return;
        var playhead = _mixer.PlayheadFrames;
        PlayheadMoved?.Invoke(playhead);
        Meters?.Invoke(_mixer.SampleMeters());

        if (_countIn && _project != null)
        {
            var remaining = _recordStartPlayhead - playhead;
            if (remaining <= 0)
            {
                _countIn = false;
                BeginTakeFile();
                Status("Recording");
            }
        }

        if (_recording && _project != null)
        {
            if (_project.Punch.Enabled && playhead >= _project.Punch.EndFrame && _project.Punch.EndFrame > _project.Punch.StartFrame)
                _ = StopRecordAsync();
            else if (_project.Loop.Enabled && _project.LoopRecording &&
                     _project.Loop.EndFrame > _project.Loop.StartFrame &&
                     playhead < _recordStartPlayhead && !_loopPassPending)
            {
                _loopPassPending = true;
                _ = RollLoopTakeAsync();
            }
            else if (_project.Loop.Enabled && playhead >= _recordStartPlayhead)
            {
                _loopPassPending = false;
            }
        }
    }

    public void Dispose()
    {
        StopTransport(true);
        _watcher.Dispose();
        _devices.Dispose();
        _inputDevice?.Dispose();
        _outputDevice?.Dispose();
    }

    private async Task RollLoopTakeAsync()
    {
        try
        {
            await FinalizeTakeAsync().ConfigureAwait(false);
            _recordStartPlayhead = _project!.Loop.StartFrame;
            BeginTakeFile();
        }
        catch (Exception ex)
        {
            Status("Could not start the next loop take: " + ex.Message);
        }
    }

    private void BeginTakeFile()
    {
        if (_project == null || _armedTrack == null) return;
        lock (_gate)
        {
            var paths = new ProjectPaths(_project.RootPath);
            paths.EnsureLayout();
            _takeSerial++;
            var name = $"take-{_takeSerial:000}-{DateTime.Now:HHmmss}.wav";
            _inProgressTakePath = Path.Combine(paths.TakesDir, name);
            _store.MarkRecording(_project.RootPath, _inProgressTakePath);
            _writer = new IncrementalWavWriter(_inProgressTakePath, _project.SampleRate, 1);
            _writerCts = new CancellationTokenSource();
            _writerTask = Task.Run(() => WriterLoop(_writerCts.Token));
            _recording = true;
            _recordStartPlayhead = _mixer?.PlayheadFrames ?? _recordStartPlayhead;
        }
    }

    private async Task<RecordedTakeResult?> FinalizeTakeAsync()
    {
        IncrementalWavWriter? writer;
        string? path;
        lock (_gate)
        {
            _recording = false;
            writer = _writer;
            path = _inProgressTakePath;
            _writer = null;
            _inProgressTakePath = null;
        }

        _writerCts?.Cancel();
        if (_writerTask != null)
        {
            try { await _writerTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        if (writer == null || path == null || _project == null || _armedTrack == null)
            return null;

        writer.FinalizeHeader();
        var frames = writer.FramesWritten;
        writer.Dispose();
        if (_project != null)
            _store.ClearRecordingMarker(_project.RootPath);

        if (frames < _project.SampleRate / 20)
        {
            Status("That take was too short to keep.");
            return null;
        }

        var take = new Take
        {
            Name = $"Take {_armedTrack.Takes.Count + 1}",
            RelativePath = Path.Combine("media", "takes", Path.GetFileName(path)).Replace('\\', '/'),
            StartFrame = Math.Max(0, _recordStartPlayhead - ReportedCompensationFrames),
            LengthFrames = frames,
            Channels = 1,
            Committed = true,
            RecordedUtc = DateTimeOffset.UtcNow
        };
        if (_project.Punch.Enabled)
        {
            take.StartFrame = _project.Punch.StartFrame;
            take.LengthFrames = Math.Min(frames, Math.Max(1, _project.Punch.EndFrame - _project.Punch.StartFrame));
        }

        _armedTrack.Takes.Add(take);
        _armedTrack.AuditionTakeId = take.Id;
        _project.Touch();
        _cache.LoadAbsolute(take.Id, path);
        RebuildMix();
        _store.Autosave(_project);
        var result = new RecordedTakeResult { Take = take, AbsolutePath = path };
        TakeCommitted?.Invoke(result);
        Status("Take saved to disk.");
        return result;
    }

    private void WriterLoop(CancellationToken token)
    {
        var buf = new float[2048];
        var mono = new float[2048];
        while (!token.IsCancellationRequested)
        {
            var n = _captureRing.Read(buf);
            if (n <= 0)
            {
                Thread.Sleep(4);
                continue;
            }

            var writer = _writer;
            if (writer == null) continue;

            int frames;
            if (_captureChannels <= 1)
            {
                frames = ResampleToProject(buf.AsSpan(0, n), 1, mono);
            }
            else
            {
                var collapsed = n / _captureChannels;
                for (var i = 0; i < collapsed; i++)
                {
                    var acc = 0f;
                    for (var c = 0; c < _captureChannels; c++)
                        acc += buf[i * _captureChannels + c];
                    mono[i] = acc / _captureChannels;
                }
                frames = ResampleToProject(mono.AsSpan(0, collapsed), 1, buf);
                buf.AsSpan(0, frames).CopyTo(mono);
            }

            if (_project != null && _armedTrack != null)
            {
                var gain = Core.Dsp.AudioMath.DbToLin(_armedTrack.InputGainDb);
                for (var i = 0; i < frames; i++)
                    mono[i] *= gain;
            }

            writer.WriteInterleavedFloat(mono.AsSpan(0, frames));
        }
        _writer?.Flush();
    }

    private int ResampleToProject(ReadOnlySpan<float> source, int channels, float[] dest)
    {
        var target = _project?.SampleRate ?? _captureRate;
        if (_captureRate == target || _captureRate <= 0)
        {
            var n = Math.Min(source.Length, dest.Length);
            source[..n].CopyTo(dest);
            return n;
        }

        var ratio = _captureRate / (double)target;
        var outFrames = (int)(source.Length / channels / ratio);
        outFrames = Math.Min(outFrames, dest.Length / channels);
        for (var i = 0; i < outFrames; i++)
        {
            var src = i * ratio;
            var i0 = (int)src;
            var frac = (float)(src - i0);
            var i1 = Math.Min(i0 + 1, source.Length / channels - 1);
            for (var c = 0; c < channels; c++)
            {
                var a = source[i0 * channels + c];
                var b = source[i1 * channels + c];
                dest[i * channels + c] = a + (b - a) * frac;
            }
        }
        return outFrames * channels;
    }

    private void StartOutput()
    {
        if (_project == null) return;
        EnsureMixer(_project.SampleRate);
        RebuildMix();
        if (_output == null)
        {
            OpenOutput(_mixer!);
            _mixer!.SetMonitor(_monitorRing);
        }
        if (_output!.PlaybackState != PlaybackState.Playing)
            _output.Play();
    }

    private void StartInputMeter()
    {
        if (_capture != null) return;
        _inputDevice ??= ResolveInput(_config.InputDeviceId);
        if (_inputDevice == null) throw new InvalidOperationException("Choose a microphone first.");
        var share = _config.ExclusiveMode ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared;
        _capture = new WasapiCapture(_inputDevice, share == AudioClientShareMode.Exclusive, _config.BufferMilliseconds);
        _captureRate = _capture.WaveFormat.SampleRate;
        _captureChannels = _capture.WaveFormat.Channels;
        _capture.DataAvailable += OnCaptureData;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null)
            {
                OnDeviceLost(new DeviceLostInfo
                {
                    DeviceName = _inputDevice.FriendlyName,
                    WasInput = true,
                    RecoveryMessage = "The microphone stopped. Recording was saved up to the last written audio. Choose the mic again on Audio Setup."
                });
            }
        };
        _capture.StartRecording();
        UpdateCompensation();
    }

    private void OnCaptureData(object? sender, WaveInEventArgs e)
    {
        var frames = ConvertCapture(e.Buffer, e.BytesRecorded, _captureConvert);
        if (frames <= 0) return;
        var samples = frames * _captureChannels;
        _captureRing.Write(_captureConvert.AsSpan(0, samples));
        if (_config.SoftwareMonitor)
            _monitorRing.Write(_captureConvert.AsSpan(0, Math.Min(samples, frames)));

        var peak = 0f;
        for (var i = 0; i < samples; i++)
            peak = Math.Max(peak, Math.Abs(_captureConvert[i]));
        _mixer?.NotifyInputPeak(peak);
    }

    private int ConvertCapture(byte[] buffer, int bytes, float[] dest)
    {
        var fmt = _capture!.WaveFormat;
        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            var count = bytes / 4;
            count = Math.Min(count, dest.Length);
            Buffer.BlockCopy(buffer, 0, dest, 0, count * 4);
            return count / Math.Max(1, fmt.Channels);
        }

        if (fmt.BitsPerSample == 16)
        {
            var count = bytes / 2;
            count = Math.Min(count, dest.Length);
            for (var i = 0; i < count; i++)
                dest[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
            return count / Math.Max(1, fmt.Channels);
        }

        if (fmt.BitsPerSample == 24)
        {
            var count = bytes / 3;
            count = Math.Min(count, dest.Length);
            for (var i = 0; i < count; i++)
            {
                var v = buffer[i * 3] | (buffer[i * 3 + 1] << 8) | (buffer[i * 3 + 2] << 16);
                if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                dest[i] = v / 8388608f;
            }
            return count / Math.Max(1, fmt.Channels);
        }

        if (fmt.BitsPerSample == 32)
        {
            var count = bytes / 4;
            count = Math.Min(count, dest.Length);
            for (var i = 0; i < count; i++)
                dest[i] = BitConverter.ToInt32(buffer, i * 4) / 2147483648f;
            return count / Math.Max(1, fmt.Channels);
        }

        return 0;
    }

    private void OpenOutput(ISampleProvider provider)
    {
        _output?.Dispose();
        _outputDevice ??= ResolveOutput(_config.OutputDeviceId);
        if (_outputDevice == null) throw new InvalidOperationException("Choose headphones or speakers first.");
        var share = _config.ExclusiveMode ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared;
        _output = new WasapiOut(_outputDevice, share, true, Math.Clamp(_config.BufferMilliseconds, 8, 80));
        _output.Init(provider);
        _output.PlaybackStopped += (_, e) =>
        {
            _playing = false;
            if (e.Exception != null)
            {
                OnDeviceLost(new DeviceLostInfo
                {
                    DeviceName = _outputDevice.FriendlyName,
                    WasInput = false,
                    RecoveryMessage = "Headphones or speakers stopped. Playback was halted so the app would not switch devices by itself."
                });
            }
        };
        UpdateCompensation();
    }

    private void EnsureMixer(int sampleRate)
    {
        if (_mixer == null || _mixer.WaveFormat.SampleRate != sampleRate)
            _mixer = new ProjectMixer(sampleRate);
    }

    private void RebuildMix()
    {
        if (_project == null || _mixer == null) return;
        _mixer.SetSnapshot(ProjectMixer.Build(_project, _cache, MetronomeEnabled && !_mixer.ExportMode, _config.SoftwareMonitor, 0.7f));
        _mixer.SetMonitor(_config.SoftwareMonitor ? _monitorRing : null);
    }

    private void StopTransport(bool safeFinalize)
    {
        _playing = false;
        _countIn = false;
        if (safeFinalize && _recording)
        {
            try { FinalizeTakeAsync().GetAwaiter().GetResult(); }
            catch { /* keep going */ }
        }
        try { _output?.Stop(); } catch { }
        try { _capture?.StopRecording(); } catch { }
        _output?.Dispose();
        _output = null;
        if (_capture != null)
        {
            _capture.DataAvailable -= OnCaptureData;
            _capture.Dispose();
            _capture = null;
        }
    }

    private void OnDeviceLost(DeviceLostInfo info)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_recording)
                    await FinalizeTakeAsync().ConfigureAwait(false);
            }
            catch { /* already stopping */ }
            StopTransport(false);
            DeviceLost?.Invoke(info);
            Status(info.RecoveryMessage);
        });
    }

    private void UpdateCompensation()
    {
        var inMs = _config.BufferMilliseconds;
        var outMs = _config.BufferMilliseconds;
        var rate = _project?.SampleRate ?? 48000;
        ReportedCompensationFrames = TimelineMath.SecondsToFrame((inMs + outMs) / 1000.0, rate) + _config.UserOffsetFrames;
        if (_project != null)
            _project.RecordingOffsetFrames = ReportedCompensationFrames;
    }

    private MMDevice ResolveInput(string? id) =>
        _devices.GetDevice(id) ?? _devices.GetDefaultInput();

    private MMDevice ResolveOutput(string? id) =>
        _devices.GetDevice(id) ?? _devices.GetDefaultOutput();

    private void Status(string message) => StatusChanged?.Invoke(message);
}

internal sealed class CachedAudioProvider : ISampleProvider
{
    private readonly CachedAudio _audio;
    private int _index;
    public CachedAudioProvider(CachedAudio audio)
    {
        _audio = audio;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.Channels);
    }
    public WaveFormat WaveFormat { get; }
    public int Read(float[] buffer, int offset, int count)
    {
        var remain = _audio.Interleaved.Length - _index;
        var n = Math.Min(count, remain);
        Array.Copy(_audio.Interleaved, _index, buffer, offset, n);
        _index += n;
        if (n < count) Array.Clear(buffer, offset + n, count - n);
        return count;
    }
}

internal sealed class TestToneProvider : ISampleProvider
{
    private readonly int _rate;
    private readonly double _seconds;
    private int _frame;
    public TestToneProvider(int rate, double seconds)
    {
        _rate = rate;
        _seconds = seconds;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
    }
    public WaveFormat WaveFormat { get; }
    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / 2;
        for (var i = 0; i < frames; i++)
        {
            var t = _frame / (double)_rate;
            var env = t < 0.02 ? t / 0.02 : t > _seconds - 0.05 ? Math.Max(0, (_seconds - t) / 0.05) : 1;
            var hz = t < _seconds * 0.5 ? 440.0 : 660.0;
            var s = (float)(0.18 * env * Math.Sin(2 * Math.PI * hz * t));
            buffer[offset + i * 2] = s;
            buffer[offset + i * 2 + 1] = s;
            _frame++;
        }
        return count;
    }
}
