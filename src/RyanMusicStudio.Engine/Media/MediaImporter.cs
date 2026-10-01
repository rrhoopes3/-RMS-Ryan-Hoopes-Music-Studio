using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Engine.IO;

namespace RyanMusicStudio.Engine.Media;

public sealed class MediaImporter
{
    public AudioMedia Import(ProjectDocument project, string sourcePath, Track? targetTrack)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("That audio file could not be found.", sourcePath);

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not ".wav" and not ".mp3")
            throw new InvalidOperationException("Drop a WAV or MP3 file.");

        var paths = new ProjectPaths(project.RootPath);
        paths.EnsureLayout();

        var media = new AudioMedia
        {
            OriginalFileName = Path.GetFileName(sourcePath)
        };

        var originalName = $"{media.Id}{ext}";
        media.OriginalRelativePath = Path.Combine("media", "originals", originalName).Replace('\\', '/');
        File.Copy(sourcePath, Path.Combine(paths.Root, media.OriginalRelativePath.Replace('/', Path.DirectorySeparatorChar)), overwrite: true);

        using var reader = new AudioFileReader(sourcePath);
        media.SourceSampleRate = reader.WaveFormat.SampleRate;
        media.Channels = reader.WaveFormat.Channels;
        media.WorkingSampleRate = project.SampleRate;
        media.BitDepth = 32;

        ISampleProvider provider = reader;
        if (reader.WaveFormat.SampleRate != project.SampleRate)
            provider = new WdlResamplingSampleProvider(reader, project.SampleRate);

        var workingRel = Path.Combine("media", "working", media.Id + ".wav").Replace('\\', '/');
        media.WorkingRelativePath = workingRel;
        var workingAbs = Path.Combine(paths.Root, workingRel.Replace('/', Path.DirectorySeparatorChar));

        var channels = provider.WaveFormat.Channels;
        using var writer = new IncrementalWavWriter(workingAbs, project.SampleRate, channels);
        var buf = new float[4096];
        int read;
        long frames = 0;
        while ((read = provider.Read(buf, 0, buf.Length)) > 0)
        {
            writer.WriteInterleavedFloat(buf.AsSpan(0, read));
            frames += read / channels;
        }
        writer.FinalizeHeader();
        media.LengthFrames = frames;
        media.Channels = channels;

        project.Media.Add(media);
        targetTrack ??= project.Tracks.FirstOrDefault(t => t.Role == TrackRole.Backing)
                        ?? project.Tracks.FirstOrDefault();
        if (targetTrack != null)
        {
            targetTrack.Clips.Add(new AudioClip
            {
                MediaId = media.Id,
                StartFrame = 0,
                SourceOffsetFrames = 0,
                LengthFrames = media.LengthFrames,
                FadeInFrames = 0,
                FadeOutFrames = Math.Min(project.SampleRate / 50, media.LengthFrames / 20)
            });
            if (targetTrack.Role == TrackRole.Backing && targetTrack.Channels == TrackChannelLayout.Mono && channels == 2)
                targetTrack.Channels = TrackChannelLayout.Stereo;
        }

        project.Touch();
        return media;
    }
}
