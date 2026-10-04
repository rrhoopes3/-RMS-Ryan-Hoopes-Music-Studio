using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Timeline;

namespace RyanMusicStudio.Core.Studio;

/// <summary>
/// Puts the rendered beat on its own backing track and tiles it under vocals.
/// Play can loop the pattern; a take is allowed to run longer than one bar.
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
    /// The one-bar studio loop is only for hearing the beat. A vocal take must
    /// keep going when that loop wraps, unlike an arrange-page loop in 1.1.0.
    /// </summary>
    public static bool StopTakeAtLoopWrap(ProjectDocument project) =>
        project.Loop.Enabled && !project.LoopRecording && !HasBed(project);

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
        project.Loop.Enabled = true;
        project.Loop.StartFrame = 0;
        project.Loop.EndFrame = cover <= barFrames ? barFrames : cover;

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

    public static void ArmBeatLoop(ProjectDocument project)
    {
        var bar = BarFrames(project);
        project.Loop.Enabled = true;
        project.Loop.StartFrame = 0;
        project.Loop.EndFrame = bar;
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
