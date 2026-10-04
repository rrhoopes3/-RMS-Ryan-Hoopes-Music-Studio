using RyanMusicStudio.Core.Model;

namespace RyanMusicStudio.Core.Persistence;

public sealed class ProjectFileDto
{
    public int FormatVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "1.0.0";
    public string Name { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string ModifiedUtc { get; set; } = "";
    public int SampleRate { get; set; } = 48000;
    public double TempoBpm { get; set; } = 90;
    public TimeSignatureDto TimeSignature { get; set; } = new();
    public long RecordingOffsetFrames { get; set; }
    public int CountInBars { get; set; } = 1;
    public int PreRollBars { get; set; } = 1;
    public bool LoopRecording { get; set; }
    public LoopDto Loop { get; set; } = new();
    public PunchDto Punch { get; set; } = new();
    public long SelectionStartFrame { get; set; }
    public long SelectionEndFrame { get; set; }
    public List<MarkerDto> Markers { get; set; } = [];
    public List<MediaDto> Media { get; set; } = [];
    public List<TrackDto> Tracks { get; set; } = [];
    public MasterDto Master { get; set; } = new();
    public StudioDto? Studio { get; set; }
}

public sealed class StudioDto
{
    public List<bool> Kick { get; set; } = [];
    public List<bool> Snare { get; set; } = [];
    public List<bool> Hat { get; set; } = [];
    public List<int> Melody { get; set; } = [];
    public int VolumePercent { get; set; } = 80;
}

public sealed class TimeSignatureDto
{
    public int Numerator { get; set; } = 4;
    public int Denominator { get; set; } = 4;
}

public sealed class LoopDto
{
    public bool Enabled { get; set; }
    public long StartFrame { get; set; }
    public long EndFrame { get; set; }
}

public sealed class PunchDto
{
    public bool Enabled { get; set; }
    public long StartFrame { get; set; }
    public long EndFrame { get; set; }
}

public sealed class MarkerDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long Frame { get; set; }
    public string Lyrics { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class MediaDto
{
    public string Id { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string OriginalRelativePath { get; set; } = "";
    public string WorkingRelativePath { get; set; } = "";
    public int SourceSampleRate { get; set; }
    public int WorkingSampleRate { get; set; }
    public int Channels { get; set; }
    public long LengthFrames { get; set; }
    public int BitDepth { get; set; }
}

public sealed class ClipDto
{
    public string Id { get; set; } = "";
    public string MediaId { get; set; } = "";
    public long StartFrame { get; set; }
    public long SourceOffsetFrames { get; set; }
    public long LengthFrames { get; set; }
    public long FadeInFrames { get; set; }
    public long FadeOutFrames { get; set; }
    public double StretchRatio { get; set; } = 1;
    public bool Muted { get; set; }
}

public sealed class TakeDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long StartFrame { get; set; }
    public long SourceOffsetFrames { get; set; }
    public long LengthFrames { get; set; }
    public int Channels { get; set; }
    public string RecordedUtc { get; set; } = "";
    public bool Committed { get; set; } = true;
}

public sealed class CompRegionDto
{
    public string Id { get; set; } = "";
    public string TakeId { get; set; } = "";
    public long TimelineStartFrame { get; set; }
    public long SourceOffsetFrames { get; set; }
    public long LengthFrames { get; set; }
    public long FadeInFrames { get; set; }
    public long FadeOutFrames { get; set; }
}

public sealed class EffectDto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Bypass { get; set; }
    public Dictionary<string, double> Parameters { get; set; } = [];
}

public sealed class TrackDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Color { get; set; } = "";
    public int Channels { get; set; } = 2;
    public bool Armed { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public double GainDb { get; set; }
    public double Pan { get; set; }
    public double InputGainDb { get; set; }
    public string? AuditionTakeId { get; set; }
    public List<ClipDto> Clips { get; set; } = [];
    public List<TakeDto> Takes { get; set; } = [];
    public List<CompRegionDto> Comp { get; set; } = [];
    public List<EffectDto> Effects { get; set; } = [];
}

public sealed class MasterDto
{
    public double GainDb { get; set; }
    public bool LimiterEnabled { get; set; } = true;
    public double LimiterCeilingDb { get; set; } = -1;
}

public static class ProjectMapper
{
    public static ProjectFileDto ToDto(ProjectDocument p) => new()
    {
        FormatVersion = p.FormatVersion,
        AppVersion = p.AppVersion,
        Name = p.Name,
        CreatedUtc = p.CreatedUtc.ToString("O"),
        ModifiedUtc = p.ModifiedUtc.ToString("O"),
        SampleRate = p.SampleRate,
        TempoBpm = p.TempoBpm,
        TimeSignature = new TimeSignatureDto
        {
            Numerator = p.TimeSignature.Numerator,
            Denominator = p.TimeSignature.Denominator
        },
        RecordingOffsetFrames = p.RecordingOffsetFrames,
        CountInBars = p.CountInBars,
        PreRollBars = p.PreRollBars,
        LoopRecording = p.LoopRecording,
        Loop = new LoopDto { Enabled = p.Loop.Enabled, StartFrame = p.Loop.StartFrame, EndFrame = p.Loop.EndFrame },
        Punch = new PunchDto { Enabled = p.Punch.Enabled, StartFrame = p.Punch.StartFrame, EndFrame = p.Punch.EndFrame },
        SelectionStartFrame = p.SelectionStartFrame,
        SelectionEndFrame = p.SelectionEndFrame,
        Markers = p.Markers.Select(m => new MarkerDto
        {
            Id = m.Id, Name = m.Name, Frame = m.Frame, Lyrics = m.Lyrics, Notes = m.Notes
        }).ToList(),
        Media = p.Media.Select(m => new MediaDto
        {
            Id = m.Id,
            OriginalFileName = m.OriginalFileName,
            OriginalRelativePath = m.OriginalRelativePath,
            WorkingRelativePath = m.WorkingRelativePath,
            SourceSampleRate = m.SourceSampleRate,
            WorkingSampleRate = m.WorkingSampleRate,
            Channels = m.Channels,
            LengthFrames = m.LengthFrames,
            BitDepth = m.BitDepth
        }).ToList(),
        Tracks = p.Tracks.Select(ToTrackDto).ToList(),
        Master = new MasterDto
        {
            GainDb = p.Master.GainDb,
            LimiterEnabled = p.Master.LimiterEnabled,
            LimiterCeilingDb = p.Master.LimiterCeilingDb
        },
        Studio = ToStudioDto(p.Studio)
    };

    public static ProjectDocument FromDto(ProjectFileDto d) => new()
    {
        FormatVersion = d.FormatVersion,
        AppVersion = d.AppVersion,
        Name = d.Name,
        CreatedUtc = ParseTime(d.CreatedUtc),
        ModifiedUtc = ParseTime(d.ModifiedUtc),
        SampleRate = d.SampleRate,
        TempoBpm = d.TempoBpm,
        TimeSignature = new TimeSignature
        {
            Numerator = d.TimeSignature.Numerator,
            Denominator = d.TimeSignature.Denominator
        },
        RecordingOffsetFrames = d.RecordingOffsetFrames,
        CountInBars = d.CountInBars,
        PreRollBars = d.PreRollBars,
        LoopRecording = d.LoopRecording,
        Loop = new LoopRegion { Enabled = d.Loop.Enabled, StartFrame = d.Loop.StartFrame, EndFrame = d.Loop.EndFrame },
        Punch = new PunchRegion { Enabled = d.Punch.Enabled, StartFrame = d.Punch.StartFrame, EndFrame = d.Punch.EndFrame },
        SelectionStartFrame = d.SelectionStartFrame,
        SelectionEndFrame = d.SelectionEndFrame,
        Markers = d.Markers.Select(m => new Marker
        {
            Id = m.Id, Name = m.Name, Frame = m.Frame, Lyrics = m.Lyrics, Notes = m.Notes
        }).ToList(),
        Media = d.Media.Select(m => new AudioMedia
        {
            Id = m.Id,
            OriginalFileName = m.OriginalFileName,
            OriginalRelativePath = m.OriginalRelativePath,
            WorkingRelativePath = m.WorkingRelativePath,
            SourceSampleRate = m.SourceSampleRate,
            WorkingSampleRate = m.WorkingSampleRate,
            Channels = m.Channels,
            LengthFrames = m.LengthFrames,
            BitDepth = m.BitDepth
        }).ToList(),
        Tracks = d.Tracks.Select(FromTrackDto).ToList(),
        Master = new MasterBus
        {
            GainDb = d.Master.GainDb,
            LimiterEnabled = d.Master.LimiterEnabled,
            LimiterCeilingDb = d.Master.LimiterCeilingDb
        },
        Studio = FromStudioDto(d.Studio)
    };

    private static TrackDto ToTrackDto(Track t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Role = t.Role.ToString(),
        Color = t.Color,
        Channels = (int)t.Channels,
        Armed = t.Armed,
        Mute = t.Mute,
        Solo = t.Solo,
        GainDb = t.GainDb,
        Pan = t.Pan,
        InputGainDb = t.InputGainDb,
        AuditionTakeId = t.AuditionTakeId,
        Clips = t.Clips.Select(c => new ClipDto
        {
            Id = c.Id,
            MediaId = c.MediaId,
            StartFrame = c.StartFrame,
            SourceOffsetFrames = c.SourceOffsetFrames,
            LengthFrames = c.LengthFrames,
            FadeInFrames = c.FadeInFrames,
            FadeOutFrames = c.FadeOutFrames,
            StretchRatio = c.StretchRatio,
            Muted = c.Muted
        }).ToList(),
        Takes = t.Takes.Select(x => new TakeDto
        {
            Id = x.Id,
            Name = x.Name,
            RelativePath = x.RelativePath,
            StartFrame = x.StartFrame,
            SourceOffsetFrames = x.SourceOffsetFrames,
            LengthFrames = x.LengthFrames,
            Channels = x.Channels,
            RecordedUtc = x.RecordedUtc.ToString("O"),
            Committed = x.Committed
        }).ToList(),
        Comp = t.Comp.Regions.Select(r => new CompRegionDto
        {
            Id = r.Id,
            TakeId = r.TakeId,
            TimelineStartFrame = r.TimelineStartFrame,
            SourceOffsetFrames = r.SourceOffsetFrames,
            LengthFrames = r.LengthFrames,
            FadeInFrames = r.FadeInFrames,
            FadeOutFrames = r.FadeOutFrames
        }).ToList(),
        Effects = t.Effects.Select(e => new EffectDto
        {
            Id = e.Id,
            Kind = e.Kind.ToString(),
            Bypass = e.Bypass,
            Parameters = new Dictionary<string, double>(e.Parameters)
        }).ToList()
    };

    private static Track FromTrackDto(TrackDto t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Role = Enum.TryParse<TrackRole>(t.Role, out var role) ? role : TrackRole.Audio,
        Color = t.Color,
        Channels = t.Channels <= 1 ? TrackChannelLayout.Mono : TrackChannelLayout.Stereo,
        Armed = t.Armed,
        Mute = t.Mute,
        Solo = t.Solo,
        GainDb = t.GainDb,
        Pan = t.Pan,
        InputGainDb = t.InputGainDb,
        AuditionTakeId = t.AuditionTakeId,
        Clips = t.Clips.Select(c => new AudioClip
        {
            Id = c.Id,
            MediaId = c.MediaId,
            StartFrame = c.StartFrame,
            SourceOffsetFrames = c.SourceOffsetFrames,
            LengthFrames = c.LengthFrames,
            FadeInFrames = c.FadeInFrames,
            FadeOutFrames = c.FadeOutFrames,
            StretchRatio = c.StretchRatio,
            Muted = c.Muted
        }).ToList(),
        Takes = t.Takes.Select(x => new Take
        {
            Id = x.Id,
            Name = x.Name,
            RelativePath = x.RelativePath,
            StartFrame = x.StartFrame,
            SourceOffsetFrames = x.SourceOffsetFrames,
            LengthFrames = x.LengthFrames,
            Channels = x.Channels,
            RecordedUtc = ParseTime(x.RecordedUtc),
            Committed = x.Committed
        }).ToList(),
        Comp = new Comp
        {
            Regions = t.Comp.Select(r => new CompRegion
            {
                Id = r.Id,
                TakeId = r.TakeId,
                TimelineStartFrame = r.TimelineStartFrame,
                SourceOffsetFrames = r.SourceOffsetFrames,
                LengthFrames = r.LengthFrames,
                FadeInFrames = r.FadeInFrames,
                FadeOutFrames = r.FadeOutFrames
            }).ToList()
        },
        Effects = t.Effects.Select(e => new EffectSlot
        {
            Id = e.Id,
            Kind = Enum.TryParse<EffectKind>(e.Kind, out var kind) ? kind : EffectKind.HighPass,
            Bypass = e.Bypass,
            Parameters = new Dictionary<string, double>(e.Parameters)
        }).ToList()
    };

    private static StudioDto ToStudioDto(SongSketch sketch)
    {
        sketch.Normalize();
        return new StudioDto
        {
            Kick = sketch.Kick.ToList(),
            Snare = sketch.Snare.ToList(),
            Hat = sketch.Hat.ToList(),
            Melody = sketch.Melody.ToList(),
            VolumePercent = sketch.VolumePercent
        };
    }

    private static SongSketch FromStudioDto(StudioDto? dto)
    {
        var sketch = new SongSketch();
        if (dto == null) return sketch;
        sketch.Kick = dto.Kick;
        sketch.Snare = dto.Snare;
        sketch.Hat = dto.Hat;
        sketch.Melody = dto.Melody;
        sketch.VolumePercent = dto.VolumePercent;
        sketch.Normalize();
        return sketch;
    }

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.TryParse(value, out var t) ? t : DateTimeOffset.UtcNow;
}
