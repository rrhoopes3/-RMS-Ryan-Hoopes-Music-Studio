using System.Text.Json;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;

namespace RyanMusicStudio.Core.Editing;

public sealed class UndoStack
{
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private readonly int _limit;

    public UndoStack(int limit = 80) => _limit = Math.Max(8, limit);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;

    public void RememberBeforeChange(ProjectDocument project)
    {
        _undo.Add(ProjectSerializer.ToJson(project));
        if (_undo.Count > _limit)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    public ProjectDocument? Undo(ProjectDocument current)
    {
        if (!CanUndo) return null;
        _redo.Add(ProjectSerializer.ToJson(current));
        var json = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return ProjectSerializer.FromJson(json, current.RootPath);
    }

    public ProjectDocument? Redo(ProjectDocument current)
    {
        if (!CanRedo) return null;
        _undo.Add(ProjectSerializer.ToJson(current));
        var json = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return ProjectSerializer.FromJson(json, current.RootPath);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

public static class ProjectSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string ToJson(ProjectDocument project)
    {
        var dto = ProjectMapper.ToDto(project);
        return JsonSerializer.Serialize(dto, Options);
    }

    public static ProjectDocument FromJson(string json, string rootPath)
    {
        var dto = JsonSerializer.Deserialize<ProjectFileDto>(json, Options)
                  ?? throw new InvalidDataException("Project file is empty.");
        if (dto.FormatVersion < 1)
            throw new InvalidDataException("Project format version is missing.");
        var project = ProjectMapper.FromDto(dto);
        project.RootPath = rootPath;
        project.Dirty = false;
        return project;
    }
}
