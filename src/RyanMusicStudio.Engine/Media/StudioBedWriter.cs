using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Core.Studio;
using RyanMusicStudio.Engine.IO;

namespace RyanMusicStudio.Engine.Media;

public static class StudioBedWriter
{
    public static void Write(ProjectDocument project)
    {
        if (string.IsNullOrWhiteSpace(project.RootPath))
            throw new InvalidOperationException("The song folder is not set.");

        var audio = StudioSynth.Render(project.Studio, project.SampleRate, project.TempoBpm,
            project.TimeSignature.Numerator, project.TimeSignature.Denominator);
        var paths = new ProjectPaths(project.RootPath);
        paths.EnsureLayout();
        var absolute = Path.Combine(paths.Root, StudioSong.BedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        FloatWav.Write(absolute, new CachedAudio
        {
            Interleaved = audio,
            Channels = 2,
            SampleRate = project.SampleRate
        });
        StudioSong.Place(project, audio.Length / 2);
    }
}
