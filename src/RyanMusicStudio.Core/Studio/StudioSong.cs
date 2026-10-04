using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Timeline;

namespace RyanMusicStudio.Core.Studio;

/// <summary>
/// Puts the rendered beat on the backing track and loops it, so the existing
/// mixer, timeline, and project file are what Play and Save use.
/// </summary>
public static class StudioSong
{
    public const string BedMediaId = "studio-bed";
    public const string BedClipId = "studio-bed-clip";
    public const string BedRelativePath = "media/working/studio-bed.wav";

    public static long BarFrames(int sampleRate, double tempoBpm)
    {
        var step = Math.Max(1, TimelineMath.SamplesPerBeat(sampleRate, tempoBpm) / 4);
        return step * SongSketch.StepCount;
    }

    public static int StepAt(int sampleRate, double tempoBpm, long frame)
    {
        var step = Math.Max(1, TimelineMath.SamplesPerBeat(sampleRate, tempoBpm) / 4);
        if (frame < 0) frame = 0;
        return (int)((frame / step) % SongSketch.StepCount);
    }

    public static void Place(ProjectDocument project, long frames)
    {
        project.Studio.Normalize();
        frames = Math.Max(1, frames);
        project.Master.GainDb = project.Studio.VolumeDb;
        project.Loop.Enabled = true;
        project.Loop.StartFrame = 0;
        project.Loop.EndFrame = frames;

        var beat = project.Tracks.FirstOrDefault(t => t.Role == TrackRole.Backing);
        if (beat == null)
        {
            beat = new Track
            {
                Name = "Beat",
                Role = TrackRole.Backing,
                Color = "#0E7C66",
                Channels = TrackChannelLayout.Stereo,
                Armed = false
            };
            project.Tracks.Insert(0, beat);
        }

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
        media.LengthFrames = frames;

        beat.Clips.RemoveAll(c => c.MediaId == BedMediaId || c.Id == BedClipId);
        beat.Clips.Add(new AudioClip
        {
            Id = BedClipId,
            MediaId = BedMediaId,
            StartFrame = 0,
            SourceOffsetFrames = 0,
            LengthFrames = frames
        });
        project.Touch();
    }

    public static void ApplyVolume(ProjectDocument project)
    {
        project.Studio.Normalize();
        project.Master.GainDb = project.Studio.VolumeDb;
        project.Touch();
    }
}
