namespace RyanMusicStudio.Core.Persistence;

public sealed class ProjectPaths
{
    public string Root { get; }
    public string ProjectFile => Path.Combine(Root, "project.json");
    public string ProjectBackup => Path.Combine(Root, "project.json.bak");
    public string AutosaveDir => Path.Combine(Root, "autosave");
    public string AutosaveFile => Path.Combine(AutosaveDir, "project.json");
    public string OriginalsDir => Path.Combine(Root, "media", "originals");
    public string WorkingDir => Path.Combine(Root, "media", "working");
    public string TakesDir => Path.Combine(Root, "media", "takes");
    public string WaveformCacheDir => Path.Combine(Root, "cache", "waveforms");
    public string RecoveryDir => Path.Combine(Root, "recovery");
    public string CrashMarker => Path.Combine(RecoveryDir, "unclean-exit.lock");
    public string RecordingMarker => Path.Combine(RecoveryDir, "recording.lock");

    public ProjectPaths(string root) => Root = Path.GetFullPath(root);

    public void EnsureLayout()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AutosaveDir);
        Directory.CreateDirectory(OriginalsDir);
        Directory.CreateDirectory(WorkingDir);
        Directory.CreateDirectory(TakesDir);
        Directory.CreateDirectory(WaveformCacheDir);
        Directory.CreateDirectory(RecoveryDir);
    }

    public static bool LooksLikeProject(string root)
    {
        if (!Directory.Exists(root)) return false;
        return File.Exists(Path.Combine(root, "project.json"))
               || File.Exists(Path.Combine(root, "autosave", "project.json"));
    }
}
