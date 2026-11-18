using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Timeline;

namespace RyanMusicStudio.Core.Studio;

/// <summary>
/// Puts the rendered beat on its own backing track and tiles it under vocals.
/// Live playback repeats the beat by itself, so the transport loop stays a user choice.
/// </summary>
public static class StudioSong
{
    public const string BedMediaId = "studio-bed";
    public const string BedClipId = "studio-bed-clip";
    public const string BedRelativePath = "media/working/studio-bed.wav";

    public static long BarFrames(int sampleRate, double tempoBpm, int numerator = 4, int denominator = 4)
    {
        if (numerator != 4 || denominator != 4)
            return Math.Max(SongSketch.StepCount, TimelineMath.SamplesPerBar(sampleRate, tempoBpm, numerator, denominator));
        // Preserve the sample timing of existing 4/4 patterns.
        var step = Math.Max(1, TimelineMath.SamplesPerBeat(sampleRate, tempoBpm) / 4);
        return step * SongSketch.StepCount;
    }

    public static long BarFrames(ProjectDocument project) =>
        BarFrames(project.SampleRate, project.TempoBpm, project.TimeSignature.Numerator, project.TimeSignature.Denominator);

    public static int StepAt(int sampleRate, double tempoBpm, long frame, int numerator = 4, int denominator = 4)
    {
        var bar = BarFrames(sampleRate, tempoBpm, numerator, denominator);
        if (frame < 0) frame = 0;
        return (int)(((frame % bar + 1) * SongSketch.StepCount - 1) / bar);
    }

    public static bool HasBed(ProjectDocument project) =>
        project.Media.Any(m => m.Id == BedMediaId);

    /// <summary>
    /// A take running past a transport loop would be out of time with the backing that jumped back.
    /// </summary>
    public static bool StopTakeAtLoopWrap(ProjectDocument project) =>
        project.Loop.Enabled && !project.LoopRecording && project.Loop.EndFrame > project.Loop.StartFrame;

    /// <summary>Songs saved by this version or later never carry an automatic beat loop.</summary>
    public static readonly Version LoopCleanupDoneIn = new(1, 2, 1);

    /// <summary>
    /// Songs saved before 1.2.1 do not record which version wrote them ("1.0.0" is the default),
    /// so they may still hold a loop an older studio build turned on by itself.
    /// </summary>
    public static bool SavedBeforeLoopCleanup(ProjectDocument project) =>
        !Version.TryParse(project.AppVersion, out var written) || written < LoopCleanupDoneIn;

    /// <summary>
    /// Earlier studio builds switched on a transport loop just to repeat the beat: from the top,
    /// over whole bars at the tempo of the day. A later tempo change kept those frames, so the
    /// end can be whole bars at any tempo the app allows. That loop cuts a longer vocal off.
    /// Songs saved by 1.2.1 or later are left alone, so a loop the singer set is kept.
    /// </summary>
    public static bool IsBeatPreviewLoop(ProjectDocument project)
    {
        if (!HasBed(project) || !project.Loop.Enabled || project.Loop.StartFrame != 0) return false;
        if (!SavedBeforeLoopCleanup(project)) return false;
        var end = project.Loop.EndFrame;
        if (end <= 0) return false;
        var (num, den) = (project.TimeSignature.Numerator, project.TimeSignature.Denominator);
        if (end % BarFrames(project.SampleRate, project.TempoBpm, num, den) == 0) return true;
        for (var bpm = 60; bpm <= 180; bpm++)
        {
            if (end % BarFrames(project.SampleRate, bpm, num, den) == 0) return true;
        }
        return false;
    }

    public static bool ClearBeatPreviewLoop(ProjectDocument project)
    {
        if (!IsBeatPreviewLoop(project)) return false;
        project.Loop.Enabled = false;
        return true;
    }

    /// <summary>The range Loop on should use: the marked range, otherwise the whole song.</summary>
    public static (long Start, long End) LoopRange(ProjectDocument project, long selectionStart, long selectionEnd)
    {
        if (selectionEnd > selectionStart)
            return (Math.Max(0, selectionStart), selectionEnd);
        return (0, Math.Max(BarFrames(project), project.LengthFrames()));
    }

    /// <summary>
    /// Keeps a loop on the same beats of the pattern when the tempo changes. Recorded and imported
    /// audio does not stretch with the tempo, so once a song has any, the loop stays where it is,
    /// on the audio the singer marked it around.
    /// </summary>
    public static void ScaleLoopForTempo(ProjectDocument project, double oldTempo, double newTempo)
    {
        if (!double.IsFinite(oldTempo) || !double.IsFinite(newTempo) || oldTempo <= 0 || newTempo <= 0) return;
        if (project.Loop.EndFrame <= project.Loop.StartFrame) return;
        if (project.Tracks.Any(HasSingerAudio)) return;
        var ratio = oldTempo / newTempo;
        project.Loop.StartFrame = (long)Math.Round(project.Loop.StartFrame * ratio);
        project.Loop.EndFrame = Math.Max(project.Loop.StartFrame + 1, (long)Math.Round(project.Loop.EndFrame * ratio));
    }

    /// <summary>True when the rendered beat would come out different: pattern, volume, tempo or meter.</summary>
    public static bool BedDiffers(ProjectDocument a, ProjectDocument b) =>
        Math.Abs(a.TempoBpm - b.TempoBpm) > 1e-9 ||
        a.SampleRate != b.SampleRate ||
        a.TimeSignature.Numerator != b.TimeSignature.Numerator ||
        a.TimeSignature.Denominator != b.TimeSignature.Denominator ||
        System.Text.Json.JsonSerializer.Serialize(a.Studio) != System.Text.Json.JsonSerializer.Serialize(b.Studio);

    public static bool LooksEmpty(ProjectDocument project)
    {
        if (!project.Studio.IsEmpty) return false;
        return !project.Tracks.Any(HasSingerAudio);
    }

    public static void Place(ProjectDocument project, long barFrames)
    {
        project.Studio.Normalize();
        barFrames = Math.Max(1, barFrames);
        ApplyVolume(project, touch: false);

        var cover = CoverFrames(project, barFrames);
        var beat = FindOrCreateBeatTrack(project);
        var media = project.Media.FirstOrDefault(m => m.Id == BedMediaId);
        if (media == null)
        {
            media = new AudioMedia { Id = BedMediaId };
            project.Media.Add(media);
        }

        media.OriginalFileName = "studio-bed.wav";
        media.OriginalRelativePath = BedRelativePath;
        media.WorkingRelativePath = BedRelativePath;
        media.SourceSampleRate = project.SampleRate;
        media.WorkingSampleRate = project.SampleRate;
        media.Channels = 2;
        media.BitDepth = 32;
        media.LengthFrames = barFrames;

        beat.Clips.RemoveAll(IsBedClip);
        var tiles = Math.Max(1, (int)Math.Ceiling(cover / (double)barFrames));
        for (var i = 0; i < tiles; i++)
        {
            var start = i * barFrames;
            beat.Clips.Add(new AudioClip
            {
                Id = i == 0 ? BedClipId : BedClipId + "-" + i,
                MediaId = BedMediaId,
                StartFrame = start,
                SourceOffsetFrames = 0,
                LengthFrames = barFrames
            });
        }

        project.Touch();
    }

    public static bool NeedsFit(ProjectDocument project)
    {
        var bar = BarFrames(project);
        var cover = CoverFrames(project, bar);
        var beat = project.Tracks.FirstOrDefault(t => t.Clips.Any(IsBedClip));
        if (beat == null) return HasBed(project) || !project.Studio.IsEmpty;
        var media = project.Media.FirstOrDefault(m => m.Id == BedMediaId);
        if (media == null || media.LengthFrames != bar || media.WorkingSampleRate != project.SampleRate ||
            media.Channels != 2 || media.WorkingRelativePath != BedRelativePath)
            return true;

        // Transport loops are user choices. Fit only describes the generated audio:
        // complete, consecutive bars covering the song, with no gaps or stale tiles.
        var tiles = beat.Clips.Where(IsBedClip).OrderBy(c => c.StartFrame).ToList();
        if (tiles.Count != Math.Max(1, (int)Math.Ceiling(cover / (double)bar))) return true;
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (tile.MediaId != BedMediaId || tile.StartFrame != i * bar ||
                tile.SourceOffsetFrames != 0 || tile.LengthFrames != bar)
                return true;
        }
        return false;
    }

    public static void ApplyVolume(ProjectDocument project) => ApplyVolume(project, touch: true);

    public static void ApplyVolume(ProjectDocument project, bool touch)
    {
        project.Studio.Normalize();
        project.Master.GainDb = project.Studio.VolumeDb;
        if (touch)
            project.Touch();
    }

    public static long CoverFrames(ProjectDocument project, long barFrames)
    {
        barFrames = Math.Max(1, barFrames);
        long end = barFrames;
        foreach (var track in project.Tracks)
        {
            foreach (var take in track.Takes)
                end = Math.Max(end, take.StartFrame + take.LengthFrames);
            foreach (var region in track.Comp.Regions)
                end = Math.Max(end, region.EndFrame);
            foreach (var clip in track.Clips)
            {
                if (IsBedClip(clip)) continue;
                end = Math.Max(end, clip.EndFrame);
            }
        }

        var tiles = Math.Max(1, (int)Math.Ceiling(end / (double)barFrames));
        return tiles * barFrames;
    }

    public static Track FindOrCreateBeatTrack(ProjectDocument project)
    {
        var owned = project.Tracks.FirstOrDefault(t => t.Clips.Any(IsBedClip));
        if (owned != null) return owned;

        var empty = project.Tracks.FirstOrDefault(t =>
            t.Role == TrackRole.Backing &&
            t.Takes.Count == 0 &&
            t.Comp.Regions.Count == 0 &&
            t.Clips.Count == 0);
        if (empty != null) return empty;

        var beat = new Track
        {
            Name = "Beat",
            Role = TrackRole.Backing,
            Color = "#0E7C66",
            Channels = TrackChannelLayout.Stereo,
            Armed = false
        };
        var firstBacking = project.Tracks.FindIndex(t => t.Role == TrackRole.Backing);
        if (firstBacking >= 0)
            project.Tracks.Insert(firstBacking, beat);
        else
            project.Tracks.Insert(0, beat);
        return beat;
    }

    public static bool IsBedClip(AudioClip clip) =>
        clip.MediaId == BedMediaId ||
        clip.Id == BedClipId ||
        clip.Id.StartsWith(BedClipId + "-", StringComparison.Ordinal);

    private static bool HasSingerAudio(Track track) =>
        track.Takes.Count > 0 ||
        track.Comp.Regions.Count > 0 ||
        track.Clips.Any(clip => !IsBedClip(clip));
}
