namespace RyanMusicStudio.Core.Model;

public enum TrackRole
{
    Audio = 0,
    Backing = 1,
    Vocal = 2,
    Reference = 3
}

public enum TrackChannelLayout
{
    Mono = 1,
    Stereo = 2
}

public enum EffectKind
{
    HighPass,
    ParametricEq,
    Compressor,
    DeEsser,
    Reverb,
    Delay,
    NoiseGate
}

public enum ExportFormat
{
    Wav16,
    Wav24,
    Mp3
}

public enum ExportScope
{
    WholeProject,
    SelectedRange,
    VocalStem,
    BackingStem
}
