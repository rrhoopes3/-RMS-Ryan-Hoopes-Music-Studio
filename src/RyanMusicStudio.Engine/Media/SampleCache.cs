using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Engine.IO;

namespace RyanMusicStudio.Engine.Media;

public sealed class SampleCache
{
    private readonly Dictionary<string, CachedAudio> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public CachedAudio? Get(string key)
    {
        lock (_gate)
            return _items.TryGetValue(key, out var v) ? v : null;
    }

    public CachedAudio LoadAbsolute(string key, string path)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(key, out var existing))
                return existing;
        }

        var loaded = FloatWav.Load(path);
        lock (_gate)
            _items[key] = loaded;
        return loaded;
    }

    public void PreloadProject(ProjectDocument project)
    {
        var paths = new ProjectPaths(project.RootPath);
        foreach (var media in project.Media)
        {
            var abs = Path.Combine(paths.Root, media.WorkingRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(abs))
                LoadAbsolute(media.Id, abs);
        }
        foreach (var track in project.Tracks)
        {
            foreach (var take in track.Takes.Where(t => t.Committed))
            {
                var abs = Path.Combine(paths.Root, take.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(abs))
                    LoadAbsolute(take.Id, abs);
            }
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
            _items.Remove(key);
    }
}
