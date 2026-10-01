using RyanMusicStudio.Core.Model;

namespace RyanMusicStudio.Core.Dsp;

public interface IPreparedEffect
{
    bool Bypass { get; }
    void Process(Span<float> interleaved, int frames, int channels);
}

public sealed class EffectChainProcessor
{
    private readonly List<IPreparedEffect> _effects = [];
    private readonly float[] _scratch = new float[8192];

    public IReadOnlyList<IPreparedEffect> Effects => _effects;

    public void Rebuild(IEnumerable<EffectSlot> slots, int sampleRate)
    {
        _effects.Clear();
        foreach (var slot in slots)
            _effects.Add(Create(slot, sampleRate));
    }

    public void Process(Span<float> interleaved, int frames, int channels)
    {
        foreach (var effect in _effects)
        {
            if (effect.Bypass) continue;
            effect.Process(interleaved, frames, channels);
        }
    }

    public static IPreparedEffect Create(EffectSlot slot, int sampleRate) => slot.Kind switch
    {
        EffectKind.HighPass => new HighPassEffect(slot, sampleRate),
        EffectKind.ParametricEq => new ParametricEqEffect(slot, sampleRate),
        EffectKind.Compressor => new CompressorEffect(slot, sampleRate),
        EffectKind.DeEsser => new DeEsserEffect(slot, sampleRate),
        EffectKind.Reverb => new ReverbEffect(slot, sampleRate),
        EffectKind.Delay => new DelayEffect(slot, sampleRate),
        EffectKind.NoiseGate => new GateEffect(slot, sampleRate),
        _ => new HighPassEffect(slot, sampleRate)
    };

    public float[] GetScratch(int count)
    {
        if (count <= _scratch.Length) return _scratch;
        return new float[count];
    }
}

internal static class Params
{
    public static double Get(EffectSlot slot, string key, double fallback) =>
        slot.Parameters.TryGetValue(key, out var v) ? v : fallback;
}

public sealed class HighPassEffect : IPreparedEffect
{
    private readonly Biquad[] _filters;
    public bool Bypass { get; }

    public HighPassEffect(EffectSlot slot, int sampleRate)
    {
        Bypass = slot.Bypass;
        var cutoff = (float)Params.Get(slot, "cutoffHz", 90);
        _filters = [new Biquad(), new Biquad()];
        _filters[0].HighPass(sampleRate, cutoff);
        _filters[1].HighPass(sampleRate, cutoff);
    }

    public void Process(Span<float> interleaved, int frames, int channels)
    {
        for (var i = 0; i < frames; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                var idx = i * channels + c;
                interleaved[idx] = _filters[Math.Min(c, _filters.Length - 1)].Process(interleaved[idx]);
            }
        }
    }
}

public sealed class ParametricEqEffect : IPreparedEffect
{
    private readonly Biquad[] _left;
    private readonly Biquad[] _right;
    public bool Bypass { get; }

    public ParametricEqEffect(EffectSlot slot, int sampleRate)
    {
        Bypass = slot.Bypass;
        _left = Build(slot, sampleRate);
        _right = Build(slot, sampleRate);
    }

    private static Biquad[] Build(EffectSlot slot, int sampleRate)
    {
        var low = new Biquad();
        low.LowShelf(sampleRate, (float)Params.Get(slot, "lowHz", 180), (float)Params.Get(slot, "lowDb", 0));
        var mid = new Biquad();
        mid.Peak(sampleRate, (float)Params.Get(slot, "midHz", 1200), (float)Params.Get(slot, "midQ", 1.0), (float)Params.Get(slot, "midDb", 0));
        var high = new Biquad();
        high.HighShelf(sampleRate, (float)Params.Get(slot, "highHz", 8000), (float)Params.Get(slot, "highDb", 0));
        return [low, mid, high];
    }

    public void Process(Span<float> interleaved, int frames, int channels)
    {
        for (var i = 0; i < frames; i++)
        {
            var l = interleaved[i * channels];
            foreach (var f in _left) l = f.Process(l);
            interleaved[i * channels] = l;
            if (channels > 1)
            {
                var r = interleaved[i * channels + 1];
                foreach (var f in _right) r = f.Process(r);
                interleaved[i * channels + 1] = r;
            }
        }
    }
}

public sealed class CompressorEffect : IPreparedEffect
{
    private readonly float _threshold;
    private readonly float _ratio;
    private readonly float _makeup;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;
    private float _env;
    public bool Bypass { get; }

    public CompressorEffect(EffectSlot slot, int sampleRate)
    {
        Bypass = slot.Bypass;
        _threshold = AudioMath.DbToLin(Params.Get(slot, "thresholdDb", -18));
        _ratio = (float)Params.Get(slot, "ratio", 3);
        _makeup = AudioMath.DbToLin(Params.Get(slot, "makeupDb", 3));
        _attackCoeff = Coeff(sampleRate, Params.Get(slot, "attackMs", 12));
        _releaseCoeff = Coeff(sampleRate, Params.Get(slot, "releaseMs", 80));
    }

    public void Process(Span<float> interleaved, int frames, int channels)
    {
        for (var i = 0; i < frames; i++)
        {
            var peak = 0f;
            for (var c = 0; c < channels; c++)
                peak = Math.Max(peak, Math.Abs(interleaved[i * channels + c]));
            var coeff = peak > _env ? _attackCoeff : _releaseCoeff;
            _env = coeff * _env + (1 - coeff) * peak;
            var gain = 1f;
            if (_env > _threshold && _ratio > 1)
            {
                var over = _env / _threshold;
                var compressed = MathF.Pow(over, 1f / _ratio);
                gain = compressed / over;
            }
            gain *= _makeup;
            for (var c = 0; c < channels; c++)
                interleaved[i * channels + c] *= gain;
        }
    }

    private static float Coeff(int sr, double ms) =>
        MathF.Exp(-1f / (float)(sr * Math.Max(0.5, ms) / 1000.0));
}

public sealed class DeEsserEffect : IPreparedEffect
{
    private readonly Biquad _detectL = new();
    private readonly Biquad _detectR = new();
    private readonly float _threshold;
    private readonly float _ratio;
    private float _env;
    public bool Bypass { get; }

    public DeEsserEffect(EffectSlot slot, int sampleRate)
    {
        Bypass = slot.Bypass;
        var freq = (float)Params.Get(slot, "freqHz", 6500);
        _detectL.BandPass(sampleRate, freq, 1.8f);
        _detectR.BandPass(sampleRate, freq, 1.8f);
        _threshold = AudioMath.DbToLin(Params.Get(slot, "thresholdDb", -20));
        _ratio = (float)Params.Get(slot, "ratio", 4);
    }

    public void Process(Span<float> interleaved, int frames, int channels)
    {
        for (var i = 0; i < frames; i++)
        {
            var l = interleaved[i * channels];
            var s = Math.Abs(_detectL.Process(l));
            if (channels > 1)
                s = Math.Max(s, Math.Abs(_detectR.Process(interleaved[i * channels + 1])));
            _env = 0.9f * _env + 0.1f * s;
            var gain = 1f;
            if (_env > _threshold)
                gain = _threshold / _env * (1f / _ratio) + (1f - 1f / _ratio);
            for (var c = 0; c < channels; c++)
                interleaved[i * channels + c] *= gain;
        }
    }
}

public sealed class DelayEffect : IPreparedEffect
{
    private readonly float[] _bufferL;
    private readonly float[] _bufferR;
    private readonly int _delaySamples;
    private readonly float _mix;
    private readonly float _feedback;
    private int _index;
    public bool Bypass { get; }

    public DelayEffect(EffectSlot slot, int sampleRate)
    {
        Bypass = slot.Bypass;
        var ms = Params.Get(slot, "timeMs", 180);
        _delaySamples = Math.Max(1, (int)(sampleRate * ms / 1000.0));
        _mix = (float)Params.Get(slot, "mix", 0.18);
        _feedback = (float)Params.Get(slot, "feedback", 0.28);
        _bufferL = new float[_delaySamples + 8];
        _bufferR = new float[_delaySamples + 8];
    }

    public void Process(Span<float> interleaved, int frames, int channels)
    {
        for (var i = 0; i < frames; i++)
        {
            var l = interleaved[i * channels];
            var r = channels > 1 ? interleaved[i * channels + 1] : l;
            var dl = _bufferL[_index];
            var dr = _bufferR[_index];
            _bufferL[_index] = l + dl * _feedback;
            _bufferR[_index] = r + dr * _feedback;
            interleaved[i * channels] = l * (1 - _mix) + dl * _mix;
            if (channels > 1)
                interleaved[i * channels + 1] = r * (1 - _mix) + dr * _mix;
            _index++;
            if (_index >= _delaySamples) _index = 0;
        }
    }
}

public sealed class GateEffect : IPreparedEffect
{
    private readonly float _threshold;
    private readonly float _attack;
    private readonly float _release;
    private float _gain = 1;
    public bool Bypass { get; }

    public GateEffect(EffectSlot slot, int sampleRate)
    {
        Bypass = slot.Bypass;
        _threshold = AudioMath.DbToLin(Params.Get(slot, "thresholdDb", -42));
        _attack = 1f - MathF.Exp(-1f / (float)(sampleRate * Params.Get(slot, "attackMs", 5) / 1000.0));
        _release = 1f - MathF.Exp(-1f / (float)(sampleRate * Params.Get(slot, "releaseMs", 80) / 1000.0));
    }

    public void Process(Span<float> interleaved, int frames, int channels)
    {
        for (var i = 0; i < frames; i++)
        {
            var peak = 0f;
            for (var c = 0; c < channels; c++)
                peak = Math.Max(peak, Math.Abs(interleaved[i * channels + c]));
            var target = peak >= _threshold ? 1f : 0.15f;
            var coeff = target > _gain ? _attack : _release;
            _gain += (target - _gain) * coeff;
            for (var c = 0; c < channels; c++)
                interleaved[i * channels + c] *= _gain;
        }
    }
}

public sealed class ReverbEffect : IPreparedEffect
{
    private readonly Comb[] _combsL;
    private readonly Comb[] _combsR;
    private readonly Allpass[] _apL;
    private readonly Allpass[] _apR;
    private readonly float _mix;
    private readonly float _damp;
    public bool Bypass { get; }

    public ReverbEffect(EffectSlot slot, int sampleRate)
    {
        Bypass = slot.Bypass;
        _mix = (float)Params.Get(slot, "mix", 0.22);
        var room = (float)Params.Get(slot, "room", 0.75);
        _damp = (float)Params.Get(slot, "damp", 0.35);
        int[] combDelay = [1557, 1617, 1491, 1422, 1277, 1356, 1188, 1116];
        int[] apDelay = [225, 556, 441, 341];
        var scale = sampleRate / 44100f;
        _combsL = combDelay.Select(d => new Comb((int)(d * scale), room)).ToArray();
        _combsR = combDelay.Select(d => new Comb((int)((d + 23) * scale), room)).ToArray();
        _apL = apDelay.Select(d => new Allpass((int)(d * scale))).ToArray();
        _apR = apDelay.Select(d => new Allpass((int)((d + 11) * scale))).ToArray();
    }

    public void Process(Span<float> interleaved, int frames, int channels)
    {
        for (var i = 0; i < frames; i++)
        {
            var l = interleaved[i * channels];
            var r = channels > 1 ? interleaved[i * channels + 1] : l;
            var mono = (l + r) * 0.5f;
            var wetL = 0f;
            var wetR = 0f;
            foreach (var c in _combsL) wetL += c.Process(mono, _damp);
            foreach (var c in _combsR) wetR += c.Process(mono, _damp);
            wetL /= _combsL.Length;
            wetR /= _combsR.Length;
            foreach (var a in _apL) wetL = a.Process(wetL);
            foreach (var a in _apR) wetR = a.Process(wetR);
            interleaved[i * channels] = l * (1 - _mix) + wetL * _mix;
            if (channels > 1)
                interleaved[i * channels + 1] = r * (1 - _mix) + wetR * _mix;
        }
    }

    private sealed class Comb
    {
        private readonly float[] _buf;
        private readonly float _feedback;
        private int _idx;
        private float _filter;

        public Comb(int len, float feedback)
        {
            _buf = new float[Math.Max(16, len)];
            _feedback = Math.Clamp(feedback, 0.1f, 0.96f);
        }

        public float Process(float x, float damp)
        {
            var y = _buf[_idx];
            _filter = y * (1 - damp) + _filter * damp;
            _buf[_idx] = x + _filter * _feedback;
            _idx++;
            if (_idx >= _buf.Length) _idx = 0;
            return y;
        }
    }

    private sealed class Allpass
    {
        private readonly float[] _buf;
        private int _idx;
        public Allpass(int len) => _buf = new float[Math.Max(8, len)];

        public float Process(float x)
        {
            var buf = _buf[_idx];
            var y = -x + buf;
            _buf[_idx] = x + buf * 0.5f;
            _idx++;
            if (_idx >= _buf.Length) _idx = 0;
            return y;
        }
    }
}

public sealed class MasterLimiter
{
    private readonly float _ceiling;
    private float _gain = 1;

    public MasterLimiter(double ceilingDb)
    {
        _ceiling = AudioMath.DbToLin(ceilingDb);
    }

    public void Process(Span<float> stereoInterleaved, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            var l = stereoInterleaved[i * 2];
            var r = stereoInterleaved[i * 2 + 1];
            var peak = Math.Max(Math.Abs(l), Math.Abs(r));
            var target = peak > _ceiling ? _ceiling / peak : 1f;
            _gain = target < _gain ? target : _gain * 0.9995f + target * 0.0005f;
            stereoInterleaved[i * 2] = l * _gain;
            stereoInterleaved[i * 2 + 1] = r * _gain;
        }
    }
}
