using RyanMusicStudio.Core.Timeline;

namespace RyanMusicStudio.Core.Model;

public sealed class TimeSignature
{
    public int Numerator { get; set; } = 4;
    public int Denominator { get; set; } = 4;
}

public sealed class LoopRegion
{
    public bool Enabled { get; set; }
    public long StartFrame { get; set; }
    public long EndFrame { get; set; }
}

public sealed class PunchRegion
{
    public bool Enabled { get; set; }
    public long StartFrame { get; set; }
    public long EndFrame { get; set; }
}

public sealed class Marker
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Section";
    public long Frame { get; set; }
    public string Lyrics { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class AudioMedia
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OriginalFileName { get; set; } = "";
    public string OriginalRelativePath { get; set; } = "";
    public string WorkingRelativePath { get; set; } = "";
    public int SourceSampleRate { get; set; }
    public int WorkingSampleRate { get; set; }
    public int Channels { get; set; } = 2;
    public long LengthFrames { get; set; }
    public int BitDepth { get; set; } = 32;
}

public sealed class AudioClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MediaId { get; set; } = "";
    public long StartFrame { get; set; }
    public long SourceOffsetFrames { get; set; }
    public long LengthFrames { get; set; }
    public long FadeInFrames { get; set; }
    public long FadeOutFrames { get; set; }
    public double StretchRatio { get; set; } = 1.0;
    public bool Muted { get; set; }

    public long EndFrame => StartFrame + LengthFrames;
}

public sealed class Take
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Take";
    public string RelativePath { get; set; } = "";
    public long StartFrame { get; set; }
    public long SourceOffsetFrames { get; set; }
    public long LengthFrames { get; set; }
    public int Channels { get; set; } = 1;
    public DateTimeOffset RecordedUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool Committed { get; set; } = true;
}

public sealed class CompRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TakeId { get; set; } = "";
    public long TimelineStartFrame { get; set; }
    public long SourceOffsetFrames { get; set; }
    public long LengthFrames { get; set; }
    public long FadeInFrames { get; set; }
    public long FadeOutFrames { get; set; }

    public long EndFrame => TimelineStartFrame + LengthFrames;
}

public sealed class Comp
{
    public List<CompRegion> Regions { get; set; } = [];
}

public sealed class EffectSlot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public EffectKind Kind { get; set; }
    public bool Bypass { get; set; }
    public Dictionary<string, double> Parameters { get; set; } = [];
}

public sealed class Track
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Track";
    public TrackRole Role { get; set; } = TrackRole.Audio;
    public string Color { get; set; } = "#C17F45";
    public TrackChannelLayout Channels { get; set; } = TrackChannelLayout.Stereo;
    public bool Armed { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public double GainDb { get; set; }
    public double Pan { get; set; }
    public double InputGainDb { get; set; }
    public string? AuditionTakeId { get; set; }
    public List<AudioClip> Clips { get; set; } = [];
    public List<Take> Takes { get; set; } = [];
    public Comp Comp { get; set; } = new();
    public List<EffectSlot> Effects { get; set; } = [];
}

public sealed class MasterBus
{
    public double GainDb { get; set; }
    public bool LimiterEnabled { get; set; } = true;
    public double LimiterCeilingDb { get; set; } = -1.0;
}

public sealed class ProjectDocument
{
    public int FormatVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "1.0.0";
    public string Name { get; set; } = "Untitled";
    public string RootPath { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public int SampleRate { get; set; } = 48000;
    public double TempoBpm { get; set; } = 90;
    public TimeSignature TimeSignature { get; set; } = new();
    public long RecordingOffsetFrames { get; set; }
    public int CountInBars { get; set; } = 1;
    public int PreRollBars { get; set; } = 1;
    public bool LoopRecording { get; set; }
    public LoopRegion Loop { get; set; } = new();
    public PunchRegion Punch { get; set; } = new();
    public long SelectionStartFrame { get; set; }
    public long SelectionEndFrame { get; set; }
    public List<Marker> Markers { get; set; } = [];
    public List<AudioMedia> Media { get; set; } = [];
    public List<Track> Tracks { get; set; } = [];
    public MasterBus Master { get; set; } = new();
    public SongSketch Studio { get; set; } = new();
    public bool Dirty { get; set; }

    public Track? FindTrack(string id) => Tracks.FirstOrDefault(t => t.Id == id);
    public AudioMedia? FindMedia(string id) => Media.FirstOrDefault(m => m.Id == id);
    public Take? FindTake(string takeId) =>
        Tracks.SelectMany(t => t.Takes).FirstOrDefault(t => t.Id == takeId);

    public Marker? MarkerAtOrBefore(long frame) =>
        Markers.Where(m => m.Frame <= frame).OrderByDescending(m => m.Frame).FirstOrDefault();

    public long LengthFrames()
    {
        long end = 0;
        foreach (var track in Tracks)
        {
            foreach (var clip in track.Clips)
                end = Math.Max(end, clip.EndFrame);
            foreach (var take in track.Takes)
                end = Math.Max(end, take.StartFrame + take.LengthFrames);
            foreach (var region in track.Comp.Regions)
                end = Math.Max(end, region.EndFrame);
        }
        // Only recorded and imported audio counts: a marked range or loop dragged past the end must not
        // pad a whole-song export with silence.
        return end;
    }

    public IEnumerable<Track> ExportableTracks(ExportScope scope)
    {
        var anySolo = Tracks.Any(t => t.Solo && !t.Mute);
        IEnumerable<Track> visible = Tracks.Where(t =>
            t.Role != TrackRole.Reference &&
            !t.Mute &&
            (!anySolo || t.Solo));

        return scope switch
        {
            ExportScope.VocalStem => visible.Where(t => t.Role == TrackRole.Vocal),
            ExportScope.BackingStem => visible.Where(t => t.Role is TrackRole.Backing or TrackRole.Audio),
            _ => visible
        };
    }

    public void Touch()
    {
        ModifiedUtc = DateTimeOffset.UtcNow;
        Dirty = true;
    }

    public static bool IsSupportedSampleRate(int sampleRate) =>
        sampleRate is 44100 or 48000;
}
