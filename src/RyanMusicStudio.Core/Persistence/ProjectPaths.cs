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

    public static bool SameDirectory(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Use Windows-compatible folder names on every platform so songs can be moved between them.
    public static string SafeFolderName(string name)
    {
        var safe = new string(name.Trim().Select(c => c < ' ' || "<>:\"/\\|?*".Contains(c) ? '-' : c).ToArray())
            .TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(safe)) safe = "New song";
        var stem = safe.Split('.')[0].TrimEnd(' ');
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                  stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             "123456789¹²³".Contains(stem[3])))
            safe = "_" + safe;
        return safe;
    }

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
