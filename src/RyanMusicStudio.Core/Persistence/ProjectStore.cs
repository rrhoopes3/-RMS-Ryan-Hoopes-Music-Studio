using RyanMusicStudio.Core.Editing;
using RyanMusicStudio.Core.Model;

namespace RyanMusicStudio.Core.Persistence;

public sealed class RecoveryOffer
{
    public required string Message { get; init; }
    public required string AutosavePath { get; init; }
    public required string? LastGoodPath { get; init; }
    public required IReadOnlyList<string> CompletedTakeFiles { get; init; }
    public bool AutosaveIsNewer { get; init; }
}

public sealed class ProjectStore
{
    public ProjectPaths Save(ProjectDocument project)
    {
        if (string.IsNullOrWhiteSpace(project.RootPath))
            throw new InvalidOperationException("Project folder is not set.");

        var paths = new ProjectPaths(project.RootPath);
        paths.EnsureLayout();
        project.ModifiedUtc = DateTimeOffset.UtcNow;
        AtomicFile.WriteAllText(paths.ProjectFile, ProjectSerializer.ToJson(project));
        project.Dirty = false;
        ClearCrashMarker(paths);
        return paths;
    }

    public ProjectPaths SaveAs(ProjectDocument project, string newRoot)
    {
        var source = string.IsNullOrWhiteSpace(project.RootPath) ? null : new ProjectPaths(project.RootPath);
        var dest = new ProjectPaths(newRoot);
        dest.EnsureLayout();
        if (source != null && Directory.Exists(source.Root) &&
            !string.Equals(source.Root, dest.Root, StringComparison.OrdinalIgnoreCase))
        {
            CopyMediaTree(source.OriginalsDir, dest.OriginalsDir);
            CopyMediaTree(source.WorkingDir, dest.WorkingDir);
            CopyMediaTree(source.TakesDir, dest.TakesDir);
        }

        project.RootPath = dest.Root;
        return Save(project);
    }

    public void Autosave(ProjectDocument project)
    {
        if (!project.Dirty || string.IsNullOrWhiteSpace(project.RootPath))
            return;
        var paths = new ProjectPaths(project.RootPath);
        paths.EnsureLayout();
        AtomicFile.WriteAllText(paths.AutosaveFile, ProjectSerializer.ToJson(project));
    }

    public ProjectDocument Open(string root)
    {
        var paths = new ProjectPaths(root);
        if (!AtomicFile.LooksLikeValidProjectJson(paths.ProjectFile))
        {
            if (AtomicFile.LooksLikeValidProjectJson(paths.AutosaveFile))
                return OpenFile(paths.AutosaveFile, paths.Root);
            if (AtomicFile.LooksLikeValidProjectJson(paths.ProjectBackup))
                return OpenFile(paths.ProjectBackup, paths.Root);
            throw new FileNotFoundException("This folder is not a Ryan Music Studio project.", paths.ProjectFile);
        }

        return OpenFile(paths.ProjectFile, paths.Root);
    }

    public RecoveryOffer? InspectRecovery(string root)
    {
        var paths = new ProjectPaths(root);
        var crash = File.Exists(paths.CrashMarker) || File.Exists(paths.RecordingMarker);
        var autosaveNewer = File.Exists(paths.AutosaveFile) && File.Exists(paths.ProjectFile) &&
                            File.GetLastWriteTimeUtc(paths.AutosaveFile) > File.GetLastWriteTimeUtc(paths.ProjectFile);
        var completedTakes = Directory.Exists(paths.TakesDir)
            ? Directory.GetFiles(paths.TakesDir, "*.wav").OrderBy(f => f).ToList()
            : [];

        if (!crash && !autosaveNewer)
            return null;

        return new RecoveryOffer
        {
            Message = crash
                ? "Ryan Music Studio did not close cleanly. You can restore the last good save. Finished takes are still in the project folder."
                : "A newer autosave is available. Restore it to keep the work from just before the app closed.",
            AutosavePath = paths.AutosaveFile,
            LastGoodPath = File.Exists(paths.ProjectFile) ? paths.ProjectFile : paths.ProjectBackup,
            CompletedTakeFiles = completedTakes,
            AutosaveIsNewer = autosaveNewer
        };
    }

    public ProjectDocument OpenAutosave(string root)
    {
        var paths = new ProjectPaths(root);
        if (!AtomicFile.LooksLikeValidProjectJson(paths.AutosaveFile))
            throw new FileNotFoundException("No valid autosave was found.", paths.AutosaveFile);
        return OpenFile(paths.AutosaveFile, paths.Root);
    }

    public void MarkUncleanExit(string root)
    {
        var paths = new ProjectPaths(root);
        paths.EnsureLayout();
        File.WriteAllText(paths.CrashMarker, DateTimeOffset.UtcNow.ToString("O"));
    }

    public void MarkRecording(string root, string takePath)
    {
        var paths = new ProjectPaths(root);
        paths.EnsureLayout();
        File.WriteAllText(paths.RecordingMarker, takePath);
    }

    public void ClearRecordingMarker(string root)
    {
        var paths = new ProjectPaths(root);
        if (File.Exists(paths.RecordingMarker))
            File.Delete(paths.RecordingMarker);
    }

    public static void ClearCrashMarker(ProjectPaths paths)
    {
        if (File.Exists(paths.CrashMarker))
            File.Delete(paths.CrashMarker);
        if (File.Exists(paths.RecordingMarker))
            File.Delete(paths.RecordingMarker);
    }

    private static ProjectDocument OpenFile(string file, string root)
    {
        var json = File.ReadAllText(file);
        var project = ProjectSerializer.FromJson(json, root);
        project.RootPath = root;
        return project;
    }

    private static void CopyMediaTree(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
    }
}
