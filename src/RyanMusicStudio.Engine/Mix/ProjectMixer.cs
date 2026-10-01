using NAudio.Wave;
using RyanMusicStudio.Core.Dsp;
using RyanMusicStudio.Core.Editing;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Timeline;
using RyanMusicStudio.Engine.IO;
using RyanMusicStudio.Engine.Media;

namespace RyanMusicStudio.Engine.Mix;

public sealed class BoundSpan
{
    public required PlaybackSpan Span { get; init; }
    public required CachedAudio Audio { get; init; }
}

public sealed class TrackMix
{
    public required string Name { get; init; }
    public required TrackRole Role { get; init; }
    public required float GainLin { get; init; }
    public required float Pan { get; init; }
    public required bool Mute { get; init; }
    public required bool Solo { get; init; }
    public required EffectChainProcessor Effects { get; init; }
    public required BoundSpan[] Spans { get; init; }
}

public sealed class MixSnapshot
{
    public required int SampleRate { get; init; }
    public required double TempoBpm { get; init; }
    public required TimeSignature TimeSignature { get; init; }
    public required LoopRegion Loop { get; init; }
    public required MasterBus Master { get; init; }
    public required TrackMix[] Tracks { get; init; }
    public required bool Metronome { get; init; }
    public required bool SoftwareMonitor { get; init; }
    public required float MonitorGain { get; init; }
}

public sealed class MeterState
{
    public float InputPeak;
    public float OutputPeakL;
    public float OutputPeakR;
    public int ClipCount;
}

public sealed class ProjectMixer : ISampleProvider
{
    private readonly float[] _trackScratch = new float[8192];
    private readonly float[] _masterScratch = new float[8192];
    private readonly object _snapGate = new();
    private MixSnapshot? _snapshot;
    private FloatRingBuffer? _monitor;
    private MasterLimiter _limiter = new(-1);
    private readonly float[] _click;
    private long _playhead;
    private readonly MeterState _meters = new();

    public ProjectMixer(int sampleRate)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        _click = BuildClick(sampleRate);
    }

    public WaveFormat WaveFormat { get; }
    public bool ExportMode { get; set; }
    public long PlayheadFrames => Interlocked.Read(ref _playhead);

    public void SetPlayhead(long frame) => Interlocked.Exchange(ref _playhead, Math.Max(0, frame));

    public void SetSnapshot(MixSnapshot snapshot)
    {
        lock (_snapGate)
        {
            _snapshot = snapshot;
            _limiter = new MasterLimiter(snapshot.Master.LimiterCeilingDb);
        }
    }

    public void SetMonitor(FloatRingBuffer? ring) => _monitor = ring;

    public MeterState SampleMeters()
    {
        return new MeterState
        {
            InputPeak = Interlocked.Exchange(ref _meters.InputPeak, 0),
            OutputPeakL = Interlocked.Exchange(ref _meters.OutputPeakL, 0),
            OutputPeakR = Interlocked.Exchange(ref _meters.OutputPeakR, 0),
            ClipCount = Interlocked.Exchange(ref _meters.ClipCount, 0)
        };
    }

    public void NotifyInputPeak(float level, bool clipped)
    {
        if (level > _meters.InputPeak)
            Interlocked.Exchange(ref _meters.InputPeak, level);
        if (clipped)
            Interlocked.Increment(ref _meters.ClipCount);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        MixSnapshot? snap;
        lock (_snapGate) snap = _snapshot;
        Array.Clear(buffer, offset, count);
        if (snap == null)
            return count;

        var totalFrames = count / 2;
        var chunkCapacity = Math.Min(_trackScratch.Length, _masterScratch.Length) / 2;
        var playhead = Interlocked.Read(ref _playhead);
        var anySolo = snap.Tracks.Any(t => t.Solo && !t.Mute);
        var loopEnabled = !ExportMode && snap.Loop.Enabled &&
                          snap.Loop.StartFrame >= 0 && snap.Loop.EndFrame > snap.Loop.StartFrame;
        var masterGain = AudioMath.DbToLin(snap.Master.GainDb);
        var peakL = 0f;
        var peakR = 0f;

        for (var rendered = 0; rendered < totalFrames;)
        {
            var frames = Math.Min(totalFrames - rendered, chunkCapacity);
            Array.Clear(_masterScratch, 0, frames * 2);

            foreach (var track in snap.Tracks)
            {
                if (ExportMode && track.Role == TrackRole.Reference)
                    continue;
                if (track.Mute) continue;
                if (anySolo && !track.Solo) continue;

                Array.Clear(_trackScratch, 0, frames * 2);
                RenderTrack(track, playhead, frames, snap.Loop, loopEnabled);
                track.Effects.Process(_trackScratch.AsSpan(0, frames * 2), frames, 2);
                MixToMaster(_trackScratch, frames, track.GainLin, track.Pan);
            }

            if (!ExportMode && snap.Metronome)
                MixMetronome(playhead, frames, snap, loopEnabled);

            if (!ExportMode && snap.SoftwareMonitor && _monitor != null)
                MixMonitor(frames, snap.MonitorGain);

            for (var i = 0; i < frames; i++)
            {
                _masterScratch[i * 2] *= masterGain;
                _masterScratch[i * 2 + 1] *= masterGain;
            }
            if (snap.Master.LimiterEnabled)
                _limiter.Process(_masterScratch.AsSpan(0, frames * 2), frames);

            for (var i = 0; i < frames; i++)
            {
                var l = _masterScratch[i * 2];
                var r = _masterScratch[i * 2 + 1];
                buffer[offset + (rendered + i) * 2] = l;
                buffer[offset + (rendered + i) * 2 + 1] = r;
                peakL = Math.Max(peakL, Math.Abs(l));
                peakR = Math.Max(peakR, Math.Abs(r));
            }

            playhead = AdvancePlayhead(playhead, frames, snap.Loop, loopEnabled);
            rendered += frames;
        }

        if (peakL > _meters.OutputPeakL) Interlocked.Exchange(ref _meters.OutputPeakL, peakL);
        if (peakR > _meters.OutputPeakR) Interlocked.Exchange(ref _meters.OutputPeakR, peakR);
        if (peakL >= 0.99f || peakR >= 0.99f) Interlocked.Increment(ref _meters.ClipCount);

        Interlocked.Exchange(ref _playhead, playhead);
        return totalFrames * 2;
    }

    public static MixSnapshot Build(ProjectDocument project, SampleCache cache, bool metronome, bool softwareMonitor, float monitorGain)
    {
        var tracks = new List<TrackMix>();
        foreach (var track in project.Tracks)
        {
            var spans = ClipEditing.ResolvePlayback(track);
            var bound = new List<BoundSpan>();
            foreach (var span in spans)
            {
                var audio = cache.Get(span.SourceId);
                if (audio != null)
                    bound.Add(new BoundSpan { Span = span, Audio = audio });
            }

            var effects = new EffectChainProcessor();
            effects.Rebuild(track.Effects, project.SampleRate);
            tracks.Add(new TrackMix
            {
                Name = track.Name,
                Role = track.Role,
                GainLin = AudioMath.DbToLin(track.GainDb),
                Pan = (float)track.Pan,
                Mute = track.Mute,
                Solo = track.Solo,
                Effects = effects,
                Spans = bound.ToArray()
            });
        }

        return new MixSnapshot
        {
            SampleRate = project.SampleRate,
            TempoBpm = project.TempoBpm,
            TimeSignature = project.TimeSignature,
            Loop = new LoopRegion
            {
                Enabled = project.Loop.Enabled,
                StartFrame = project.Loop.StartFrame,
                EndFrame = project.Loop.EndFrame
            },
            Master = new MasterBus
            {
                GainDb = project.Master.GainDb,
                LimiterEnabled = project.Master.LimiterEnabled,
                LimiterCeilingDb = project.Master.LimiterCeilingDb
            },
            Tracks = tracks.ToArray(),
            Metronome = metronome,
            SoftwareMonitor = softwareMonitor,
            MonitorGain = monitorGain
        };
    }

    private static int FramesToRender(ref long playhead, int remaining, LoopRegion loop, bool loopEnabled)
    {
        if (!loopEnabled) return remaining;
        if (playhead >= loop.EndFrame) playhead = loop.StartFrame;
        return (int)Math.Min(remaining, loop.EndFrame - playhead);
    }

    private static long AdvancePlayhead(long playhead, int frames, LoopRegion loop, bool loopEnabled)
    {
        if (!loopEnabled) return playhead + frames;
        if (playhead >= loop.EndFrame) playhead = loop.StartFrame;
        var untilEnd = loop.EndFrame - playhead;
        return frames < untilEnd
            ? playhead + frames
            : loop.StartFrame + (frames - untilEnd) % (loop.EndFrame - loop.StartFrame);
    }

    private void RenderTrack(TrackMix track, long playhead, int frames, LoopRegion loop, bool loopEnabled)
    {
        for (var rendered = 0; rendered < frames;)
        {
            var segmentFrames = FramesToRender(ref playhead, frames - rendered, loop, loopEnabled);
            foreach (var bound in track.Spans)
            {
                var span = bound.Span;
                var overlap = TimelineMath.OverlapLength(playhead, segmentFrames, span.TimelineStart, span.Length);
                if (overlap <= 0) continue;
                var destStart = (int)Math.Max(0, span.TimelineStart - playhead);
                var srcFrame = span.SourceOffset + Math.Max(0, playhead - span.TimelineStart);
                var audio = bound.Audio;
                var ratio = span.StretchRatio <= 0 ? 1.0 : span.StretchRatio;

                for (var i = 0; i < overlap; i++)
                {
                    var timeline = playhead + destStart + i;
                    var gain = span.FadeGain(timeline);
                    var src = srcFrame + (long)Math.Round(i * ratio);
                    if (src < 0 || src >= audio.Frames) continue;
                    float sl, sr;
                    if (audio.Channels == 1)
                    {
                        sl = sr = audio.Interleaved[src];
                    }
                    else
                    {
                        sl = audio.Interleaved[src * audio.Channels];
                        sr = audio.Interleaved[src * audio.Channels + 1];
                    }
                    var di = (rendered + destStart + i) * 2;
                    _trackScratch[di] += sl * gain;
                    _trackScratch[di + 1] += sr * gain;
                }
            }
            playhead += segmentFrames;
            rendered += segmentFrames;
        }
    }

    private void MixToMaster(float[] track, int frames, float gain, float pan)
    {
        var t = (Math.Clamp(pan, -1f, 1f) + 1f) * 0.5f;
        var leftG = gain * MathF.Cos(t * MathF.PI * 0.5f);
        var rightG = gain * MathF.Sin(t * MathF.PI * 0.5f);
        for (var i = 0; i < frames; i++)
        {
            _masterScratch[i * 2] += track[i * 2] * leftG;
            _masterScratch[i * 2 + 1] += track[i * 2 + 1] * rightG;
        }
    }

    private void MixMetronome(long playhead, int frames, MixSnapshot snap, bool loopEnabled)
    {
        var beat = TimelineMath.SamplesPerBeat(snap.SampleRate, snap.TempoBpm);
        if (beat <= 0) return;
        var barBeats = snap.TimeSignature.Numerator;
        for (var rendered = 0; rendered < frames;)
        {
            var segmentFrames = FramesToRender(ref playhead, frames - rendered, snap.Loop, loopEnabled);
            for (var i = 0; i < segmentFrames; i++)
            {
                var frame = playhead + i;
                var intoBeat = frame % beat;
                if (intoBeat < 0 || intoBeat >= _click.Length) continue;
                var beatIndex = frame / beat;
                var accent = beatIndex % barBeats == 0 ? 0.7f : 0.35f;
                var s = _click[intoBeat] * accent;
                _masterScratch[(rendered + i) * 2] += s;
                _masterScratch[(rendered + i) * 2 + 1] += s;
            }
            playhead += segmentFrames;
            rendered += segmentFrames;
        }
    }

    private void MixMonitor(int frames, float gain)
    {
        var needed = frames;
        Span<float> tmp = stackalloc float[Math.Min(needed, 512)];
        var dest = 0;
        while (dest < needed)
        {
            var n = Math.Min(tmp.Length, needed - dest);
            var got = _monitor!.Read(tmp[..n]);
            for (var i = 0; i < got; i++)
            {
                var s = tmp[i] * gain;
                _masterScratch[(dest + i) * 2] += s;
                _masterScratch[(dest + i) * 2 + 1] += s;
            }
            dest += got;
            if (got < n) break;
        }
    }

    private static float[] BuildClick(int sampleRate)
    {
        var n = sampleRate / 80;
        var click = new float[n];
        for (var i = 0; i < n; i++)
        {
            var env = 1f - i / (float)n;
            click[i] = env * env * MathF.Sin(2 * MathF.PI * 1000f * i / sampleRate);
        }
        return click;
    }
}
