using RyanMusicStudio.Core.Dsp;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Engine.IO;

namespace RyanMusicStudio.Engine.Media;

public static class SampleProjectBuilder
{
    public const string MediaLicense = "Original synthesized audio created for RMS. Dedicated to the public domain (CC0 1.0 Universal equivalent). Not a commercial loop pack and not affiliated with any other brand.";

    public static ProjectDocument Create(string root, int sampleRate = 48000, double tempo = 90)
    {
        Directory.CreateDirectory(root);
        var project = ProjectFactory.CreateVocalOverBeat("Practice Beat", root, tempo, 4, 4, sampleRate);
        var paths = new ProjectPaths(root);
        paths.EnsureLayout();

        var beat = BackingRenderer.RenderBeat(sampleRate, tempo, bars: 16);
        var originalRel = "media/originals/practice-beat.wav";
        var workingRel = "media/working/practice-beat.wav";
        var originalAbs = Path.Combine(root, originalRel.Replace('/', Path.DirectorySeparatorChar));
        var workingAbs = Path.Combine(root, workingRel.Replace('/', Path.DirectorySeparatorChar));
        FloatWav.Write(originalAbs, beat);
        File.Copy(originalAbs, workingAbs, overwrite: true);

        var media = new AudioMedia
        {
            OriginalFileName = "practice-beat.wav",
            OriginalRelativePath = originalRel,
            WorkingRelativePath = workingRel,
            SourceSampleRate = sampleRate,
            WorkingSampleRate = sampleRate,
            Channels = 2,
            LengthFrames = beat.Frames,
            BitDepth = 32
        };
        project.Media.Add(media);
        var backing = project.Tracks.First(t => t.Role == TrackRole.Backing);
        backing.Clips.Clear();
        backing.Clips.Add(new AudioClip
        {
            MediaId = media.Id,
            StartFrame = 0,
            LengthFrames = media.LengthFrames,
            FadeOutFrames = sampleRate / 20
        });

        var vocal = project.Tracks.First(t => t.Role == TrackRole.Vocal);
        VocalPresets.Apply(vocal, VocalPresets.Clean);

        project.Markers =
        [
            new Marker { Name = "Intro", Frame = 0, Lyrics = "Breathe. Wait for the count-in." },
            new Marker { Name = "Verse", Frame = media.LengthFrames / 4, Lyrics = "Sing the first line you want to keep." },
            new Marker { Name = "Chorus", Frame = media.LengthFrames / 2, Lyrics = "Lean a little louder here." }
        ];
        project.Loop.StartFrame = 0;
        project.Loop.EndFrame = media.LengthFrames;
        project.Dirty = true;

        File.WriteAllText(Path.Combine(root, "MEDIA-LICENSE.txt"), MediaLicense + Environment.NewLine);
        new ProjectStore().Save(project);
        return project;
    }
}
