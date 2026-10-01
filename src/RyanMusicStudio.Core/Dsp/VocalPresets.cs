using RyanMusicStudio.Core.Model;

namespace RyanMusicStudio.Core.Dsp;

public static class VocalPresets
{
    public const string Clean = "Clean";
    public const string Warm = "Warm";
    public const string Spacious = "Spacious";

    public static IReadOnlyList<string> Names { get; } = [Clean, Warm, Spacious];

    public static void Apply(Track track, string presetName)
    {
        track.Effects.Clear();
        switch (presetName)
        {
            case Warm:
                track.Effects.AddRange(WarmChain());
                break;
            case Spacious:
                track.Effects.AddRange(SpaciousChain());
                break;
            default:
                track.Effects.AddRange(CleanChain());
                break;
        }
    }

    public static List<EffectSlot> DefaultVocalChain() => CleanChain();

    private static List<EffectSlot> CleanChain() =>
    [
        Slot(EffectKind.NoiseGate, new() { ["thresholdDb"] = -46, ["attackMs"] = 4, ["releaseMs"] = 70 }),
        Slot(EffectKind.HighPass, new() { ["cutoffHz"] = 80 }),
        Slot(EffectKind.ParametricEq, new()
        {
            ["lowHz"] = 200, ["lowDb"] = 0,
            ["midHz"] = 2500, ["midQ"] = 0.9, ["midDb"] = 1.5,
            ["highHz"] = 9000, ["highDb"] = 1.0
        }),
        Slot(EffectKind.Compressor, new()
        {
            ["thresholdDb"] = -16, ["ratio"] = 2.5, ["attackMs"] = 14, ["releaseMs"] = 90, ["makeupDb"] = 2
        }),
        Slot(EffectKind.DeEsser, new() { ["freqHz"] = 7000, ["thresholdDb"] = -22, ["ratio"] = 3 }),
        Slot(EffectKind.Delay, new() { ["timeMs"] = 140, ["mix"] = 0.05, ["feedback"] = 0.15 }, bypass: true),
        Slot(EffectKind.Reverb, new() { ["mix"] = 0.12, ["room"] = 0.55, ["damp"] = 0.4 })
    ];

    private static List<EffectSlot> WarmChain() =>
    [
        Slot(EffectKind.NoiseGate, new() { ["thresholdDb"] = -44, ["attackMs"] = 5, ["releaseMs"] = 90 }),
        Slot(EffectKind.HighPass, new() { ["cutoffHz"] = 70 }),
        Slot(EffectKind.ParametricEq, new()
        {
            ["lowHz"] = 180, ["lowDb"] = 2.5,
            ["midHz"] = 900, ["midQ"] = 0.8, ["midDb"] = 1.0,
            ["highHz"] = 6500, ["highDb"] = -1.0
        }),
        Slot(EffectKind.Compressor, new()
        {
            ["thresholdDb"] = -18, ["ratio"] = 3.2, ["attackMs"] = 18, ["releaseMs"] = 110, ["makeupDb"] = 3
        }),
        Slot(EffectKind.DeEsser, new() { ["freqHz"] = 6200, ["thresholdDb"] = -20, ["ratio"] = 4 }),
        Slot(EffectKind.Delay, new() { ["timeMs"] = 160, ["mix"] = 0.08, ["feedback"] = 0.2 }),
        Slot(EffectKind.Reverb, new() { ["mix"] = 0.16, ["room"] = 0.62, ["damp"] = 0.45 })
    ];

    private static List<EffectSlot> SpaciousChain() =>
    [
        Slot(EffectKind.NoiseGate, new() { ["thresholdDb"] = -48, ["attackMs"] = 4, ["releaseMs"] = 80 }),
        Slot(EffectKind.HighPass, new() { ["cutoffHz"] = 90 }),
        Slot(EffectKind.ParametricEq, new()
        {
            ["lowHz"] = 220, ["lowDb"] = -0.5,
            ["midHz"] = 3200, ["midQ"] = 0.85, ["midDb"] = 2.0,
            ["highHz"] = 10000, ["highDb"] = 2.5
        }),
        Slot(EffectKind.Compressor, new()
        {
            ["thresholdDb"] = -14, ["ratio"] = 2.2, ["attackMs"] = 10, ["releaseMs"] = 70, ["makeupDb"] = 2
        }),
        Slot(EffectKind.DeEsser, new() { ["freqHz"] = 7500, ["thresholdDb"] = -24, ["ratio"] = 3 }),
        Slot(EffectKind.Delay, new() { ["timeMs"] = 220, ["mix"] = 0.16, ["feedback"] = 0.32 }),
        Slot(EffectKind.Reverb, new() { ["mix"] = 0.28, ["room"] = 0.82, ["damp"] = 0.28 })
    ];

    private static EffectSlot Slot(EffectKind kind, Dictionary<string, double> parameters, bool bypass = false) =>
        new() { Kind = kind, Bypass = bypass, Parameters = parameters };
}
