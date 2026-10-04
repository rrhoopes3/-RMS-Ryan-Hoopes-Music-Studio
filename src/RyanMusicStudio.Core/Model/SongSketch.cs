using RyanMusicStudio.Core.Dsp;

namespace RyanMusicStudio.Core.Model;

public enum DrumVoice
{
    Kick = 0,
    Snare = 1,
    Hat = 2
}

/// <summary>
/// One bar a beginner can edit: 16 steps, three drums, one piano note per step.
/// Melody values are scale degrees 0..7 (C D E F G A B C) or -1 for a rest.
/// </summary>
public sealed class SongSketch
{
    public const int StepCount = 16;
    public static readonly string[] NoteNames = ["C", "D", "E", "F", "G", "A", "B", "High C"];

    public List<bool> Kick { get; set; } = EmptyRow();
    public List<bool> Snare { get; set; } = EmptyRow();
    public List<bool> Hat { get; set; } = EmptyRow();
    public List<int> Melody { get; set; } = EmptyMelody();
    public int VolumePercent { get; set; } = 80;

    public bool IsEmpty =>
        Kick.All(on => !on) && Snare.All(on => !on) && Hat.All(on => !on) && Melody.All(note => note < 0);

    public float VolumeGain => Math.Clamp(VolumePercent, 0, 100) / 100f;

    public double VolumeDb => VolumePercent <= 0 ? -80 : AudioMath.LinToDb(VolumeGain);

    public List<bool> Drums(DrumVoice voice) => voice switch
    {
        DrumVoice.Kick => Kick,
        DrumVoice.Snare => Snare,
        _ => Hat
    };

    public bool ToggleDrum(DrumVoice voice, int step)
    {
        var row = Drums(voice);
        var index = ClampStep(step);
        row[index] = !row[index];
        return row[index];
    }

    public void SetMelody(int step, int degree)
    {
        var index = ClampStep(step);
        if (degree < 0 || degree >= NoteNames.Length)
        {
            Melody[index] = -1;
            return;
        }
        Melody[index] = Melody[index] == degree ? -1 : degree;
    }

    public void Normalize()
    {
        Kick = Fit(Kick, false);
        Snare = Fit(Snare, false);
        Hat = Fit(Hat, false);
        Melody = Fit(Melody, -1);
        for (var i = 0; i < Melody.Count; i++)
        {
            if (Melody[i] < -1 || Melody[i] >= NoteNames.Length)
                Melody[i] = -1;
        }
        VolumePercent = Math.Clamp(VolumePercent, 0, 100);
    }

    public static string NoteName(int degree) =>
        degree >= 0 && degree < NoteNames.Length ? NoteNames[degree] : "";

    private static int ClampStep(int step) => Math.Clamp(step, 0, StepCount - 1);

    private static List<bool> EmptyRow() => Enumerable.Repeat(false, StepCount).ToList();

    private static List<int> EmptyMelody() => Enumerable.Repeat(-1, StepCount).ToList();

    private static List<T> Fit<T>(List<T>? source, T fill)
    {
        var fitted = new List<T>(StepCount);
        for (var i = 0; i < StepCount; i++)
            fitted.Add(source != null && i < source.Count ? source[i] : fill);
        return fitted;
    }
}
