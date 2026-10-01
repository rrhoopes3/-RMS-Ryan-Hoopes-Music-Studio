using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Timeline;

namespace RyanMusicStudio.Core.Editing;

public static class ClipEditing
{
    public static AudioClip? Split(Track track, AudioClip clip, long atFrame)
    {
        if (!track.Clips.Contains(clip))
            throw new InvalidOperationException("Clip is not on this track.");
        if (atFrame <= clip.StartFrame || atFrame >= clip.EndFrame)
            return null;

        var leftLen = atFrame - clip.StartFrame;
        var right = new AudioClip
        {
            MediaId = clip.MediaId,
            StartFrame = atFrame,
            SourceOffsetFrames = clip.SourceOffsetFrames + leftLen,
            LengthFrames = clip.LengthFrames - leftLen,
            FadeInFrames = 0,
            FadeOutFrames = clip.FadeOutFrames,
            StretchRatio = clip.StretchRatio,
            Muted = clip.Muted
        };
        clip.LengthFrames = leftLen;
        clip.FadeOutFrames = Math.Min(clip.FadeOutFrames, leftLen);
        track.Clips.Add(right);
        return right;
    }

    public static void Trim(AudioClip clip, long newStart, long newEnd, long mediaLength)
    {
        if (newEnd <= newStart)
            throw new ArgumentException("Trim range must be at least one frame.");
        var delta = newStart - clip.StartFrame;
        clip.StartFrame = TimelineMath.ClampNonNegative(newStart);
        clip.SourceOffsetFrames = Math.Max(0, clip.SourceOffsetFrames + delta);
        clip.LengthFrames = newEnd - newStart;
        if (clip.SourceOffsetFrames + clip.LengthFrames > mediaLength)
            clip.LengthFrames = Math.Max(1, mediaLength - clip.SourceOffsetFrames);
        clip.FadeInFrames = Math.Min(clip.FadeInFrames, clip.LengthFrames);
        clip.FadeOutFrames = Math.Min(clip.FadeOutFrames, clip.LengthFrames);
    }

    public static void Move(AudioClip clip, long newStart)
    {
        clip.StartFrame = TimelineMath.ClampNonNegative(newStart);
    }

    public static void Slip(AudioClip clip, long sourceOffset, long mediaLength)
    {
        var maxOffset = Math.Max(0, mediaLength - clip.LengthFrames);
        clip.SourceOffsetFrames = Math.Clamp(sourceOffset, 0, maxOffset);
    }

    public static void SetFades(AudioClip clip, long fadeIn, long fadeOut)
    {
        fadeIn = Math.Max(0, fadeIn);
        fadeOut = Math.Max(0, fadeOut);
        if (fadeIn + fadeOut > clip.LengthFrames)
        {
            var scale = clip.LengthFrames / (double)(fadeIn + fadeOut);
            fadeIn = (long)Math.Floor(fadeIn * scale);
            fadeOut = clip.LengthFrames - fadeIn;
        }
        clip.FadeInFrames = fadeIn;
        clip.FadeOutFrames = fadeOut;
    }

    public static void Delete(Track track, AudioClip clip)
    {
        track.Clips.Remove(clip);
    }

    public static void Crossfade(AudioClip left, AudioClip right, long overlapFrames)
    {
        if (overlapFrames <= 0) return;
        var overlap = Math.Min(overlapFrames, Math.Min(left.LengthFrames, right.LengthFrames) / 2);
        left.FadeOutFrames = overlap;
        right.FadeInFrames = overlap;
        right.StartFrame = left.EndFrame - overlap;
    }

    public static CompRegion AddCompRegion(Track track, Take take, long timelineStart, long sourceOffset, long length)
    {
        var region = new CompRegion
        {
            TakeId = take.Id,
            TimelineStartFrame = TimelineMath.ClampNonNegative(timelineStart),
            SourceOffsetFrames = Math.Max(0, sourceOffset),
            LengthFrames = Math.Max(1, length)
        };
        track.Comp.Regions.Add(region);
        track.Comp.Regions.Sort((a, b) => a.TimelineStartFrame.CompareTo(b.TimelineStartFrame));
        return region;
    }

    public static void DeleteCompRegion(Track track, CompRegion region)
    {
        track.Comp.Regions.Remove(region);
    }

    public static Take? AuditionTake(Track track, string takeId)
    {
        var take = track.Takes.FirstOrDefault(t => t.Id == takeId);
        track.AuditionTakeId = take?.Id;
        return take;
    }

    public static IReadOnlyList<PlaybackSpan> ResolvePlayback(Track track)
    {
        if (track.Comp.Regions.Count > 0)
        {
            return track.Comp.Regions
                .OrderBy(r => r.TimelineStartFrame)
                .Select(r => new PlaybackSpan(
                    r.TakeId,
                    true,
                    r.TimelineStartFrame,
                    r.SourceOffsetFrames,
                    r.LengthFrames,
                    r.FadeInFrames,
                    r.FadeOutFrames))
                .ToList();
        }

        if (!string.IsNullOrEmpty(track.AuditionTakeId))
        {
            var take = track.Takes.FirstOrDefault(t => t.Id == track.AuditionTakeId);
            if (take != null)
                return [FromTake(take)];
        }

        var spans = new List<PlaybackSpan>();
        foreach (var clip in track.Clips.Where(c => !c.Muted).OrderBy(c => c.StartFrame))
        {
            spans.Add(new PlaybackSpan(
                clip.MediaId,
                false,
                clip.StartFrame,
                clip.SourceOffsetFrames,
                clip.LengthFrames,
                clip.FadeInFrames,
                clip.FadeOutFrames,
                clip.StretchRatio));
        }

        if (spans.Count == 0 && track.Takes.Count > 0)
        {
            var latest = track.Takes.Where(t => t.Committed).OrderBy(t => t.RecordedUtc).Last();
            spans.Add(FromTake(latest));
        }

        return spans;
    }

    private static PlaybackSpan FromTake(Take take) =>
        new(take.Id, true, take.StartFrame, take.SourceOffsetFrames, take.LengthFrames, 0, 0);
}

public readonly record struct PlaybackSpan(
    string SourceId,
    bool FromTake,
    long TimelineStart,
    long SourceOffset,
    long Length,
    long FadeIn,
    long FadeOut,
    double StretchRatio = 1.0)
{
    public long TimelineEnd => TimelineStart + Length;

    public float FadeGain(long timelineFrame)
    {
        var local = timelineFrame - TimelineStart;
        if (local < 0 || local >= Length) return 0;
        var gain = 1f;
        if (FadeIn > 0 && local < FadeIn)
            gain *= EqualPower((float)(local / (double)FadeIn));
        if (FadeOut > 0 && local >= Length - FadeOut)
            gain *= EqualPower((float)((Length - local) / (double)FadeOut));
        return gain;
    }

    private static float EqualPower(float t) => MathF.Sin(Math.Clamp(t, 0, 1) * MathF.PI * 0.5f);
}
