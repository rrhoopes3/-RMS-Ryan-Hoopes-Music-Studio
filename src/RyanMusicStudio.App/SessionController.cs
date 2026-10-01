using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using RyanMusicStudio.Core.Dsp;
using RyanMusicStudio.Core.Editing;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Core.Timeline;
using RyanMusicStudio.Engine.Audio;
using RyanMusicStudio.Engine.Devices;
using RyanMusicStudio.Engine.Export;
using RyanMusicStudio.Engine.Media;

namespace RyanMusicStudio.App;

public enum StudioPlace
{
    Home,
    Setup,
    Arrange,
    Mix,
    Export
}

public sealed class SessionController : IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly ProjectStore _store = new();
    private readonly UndoStack _undo = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly MixExporter _exporter = new();
    private UserSettings _settings = UserSettings.Load();

    public event Action? Changed;
    public event Action<string>? Banner;

    public ProjectDocument? Project { get; private set; }
    public StudioPlace Place { get; private set; } = StudioPlace.Home;
    public AudioEngine Engine => _engine;
    public UserSettings Settings => _settings;
    public string Status { get; private set; } = "Welcome to RMS.";
    public string Lyrics { get; private set; } = "";
    public string RecoveryText { get; private set; } = "";
    public double Zoom { get; set; } = 80; // pixels per second
    public SnapMode Snap { get; set; } = SnapMode.Beat;
    public string? SelectedClipId { get; set; }
    public string? SelectedTrackId { get; set; }
    public bool CompMode { get; set; }

    public IReadOnlyList<AudioDeviceInfo> Inputs { get; private set; } = [];
    public IReadOnlyList<AudioDeviceInfo> Outputs { get; private set; } = [];
    public AudioDeviceInfo? SelectedInput { get; private set; }
    public AudioDeviceInfo? SelectedOutput { get; private set; }
    public float InputPeak { get; private set; }
    public float OutputPeak { get; private set; }
    public bool Clipping { get; private set; }
    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    public SessionController()
    {
        _engine.StatusChanged += s => SetStatus(s);
        _engine.DeviceLost += info =>
        {
            SetStatus(info.RecoveryMessage);
            Banner?.Invoke(info.RecoveryMessage);
        };
        _engine.Meters += m =>
        {
            InputPeak = m.InputPeak;
            OutputPeak = Math.Max(m.OutputPeakL, m.OutputPeakR);
            Clipping = m.ClipCount > 0 || m.InputPeak >= 0.98f;
        };
        _engine.TakeCommitted += _ => Raise();
        _engine.PlayheadMoved += frame =>
        {
            if (Project != null)
            {
                var marker = Project.MarkerAtOrBefore(frame);
                Lyrics = marker == null ? "" : $"{marker.Name}: {marker.Lyrics}";
            }
        };
        _clock.Tick += (_, _) =>
        {
            _engine.TickUi();
            Raise();
        };
        _autosave.Tick += (_, _) =>
        {
            if (Project is { Dirty: true })
                _store.Autosave(Project);
        };
        _clock.Start();
        _autosave.Start();
        RefreshDevices();
        ApplySavedDevices();
    }

    public void Go(StudioPlace place)
    {
        Place = place;
        Raise();
    }

    public void RefreshDevices()
    {
        Inputs = _engine.Inputs();
        Outputs = _engine.Outputs();
        SelectedInput = Inputs.FirstOrDefault(d => d.Id == _settings.InputDeviceId) ?? Inputs.FirstOrDefault();
        SelectedOutput = Outputs.FirstOrDefault(d => d.Id == _settings.OutputDeviceId) ?? Outputs.FirstOrDefault();
        Raise();
    }

    public void ChooseInput(AudioDeviceInfo device)
    {
        SelectedInput = device;
        _settings.InputDeviceId = device.Id;
        ApplyDevices();
    }

    public void ChooseOutput(AudioDeviceInfo device)
    {
        SelectedOutput = device;
        _settings.OutputDeviceId = device.Id;
        ApplyDevices();
    }

    public void SetExclusive(bool exclusive)
    {
        _settings.ExclusiveMode = exclusive;
        ApplyDevices();
    }

    public void SetMonitor(bool on)
    {
        _settings.SoftwareMonitor = on;
        _engine.SoftwareMonitor = on;
        _settings.Save();
        Raise();
    }

    public void SetBuffer(int ms)
    {
        _settings.BufferMilliseconds = Math.Clamp(ms, 8, 80);
        ApplyDevices();
    }

    public void SetUserOffsetMs(double ms)
    {
        var rate = Project?.SampleRate ?? _settings.PreferredSampleRate;
        _settings.UserRecordingOffsetFrames = TimelineMath.SecondsToFrame(ms / 1000.0, rate);
        ApplyDevices();
    }

    public void ConfirmSetup()
    {
        _settings.AudioSetupConfirmed = true;
        _settings.Save();
        SetStatus("Audio setup saved. You can start a vocal session.");
    }

    public void PlayTestTone() => _engine.PlayTestTone();

    public async Task RecordTestTakeAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "RMS", "test-takes");
        SetStatus("Recording a short test. Sing a line…");
        var path = await _engine.RecordTestTakeAsync(TimeSpan.FromSeconds(4), folder);
        _engine.PlayFile(path);
        SetStatus("That was your test take. If you heard it, you are ready to record a song.");
    }

    public void NewVocalSession(string name, string parentFolder, double tempo, int num, int den, int sampleRate)
    {
        var root = Path.Combine(parentFolder, Sanitize(name));
        var project = ProjectFactory.CreateVocalOverBeat(name, root, tempo, num, den, sampleRate);
        VocalPresets.Apply(project.Tracks.First(t => t.Role == TrackRole.Vocal), VocalPresets.Clean);
        _store.Save(project);
        OpenLoaded(project);
        SetStatus("New vocal session ready. Drag a backing track onto the timeline.");
        Go(_settings.AudioSetupConfirmed ? StudioPlace.Arrange : StudioPlace.Setup);
    }

    public bool TryOpen(string root, bool useAutosave = false)
    {
        if (!ProjectPaths.LooksLikeProject(root))
        {
            SetStatus("That folder is not an RMS project.");
            return false;
        }

        var offer = _store.InspectRecovery(root);
        ProjectDocument project;
        if (useAutosave)
            project = _store.OpenAutosave(root);
        else if (offer != null)
        {
            RecoveryText = offer.Message + " Finished takes on disk: " + offer.CompletedTakeFiles.Count + ".";
            Banner?.Invoke(RecoveryText);
            project = File.Exists(offer.AutosavePath) ? _store.OpenAutosave(root) : _store.Open(root);
        }
        else
        {
            RecoveryText = "";
            project = _store.Open(root);
        }

        OpenLoaded(project);
        return true;
    }

    public void Save()
    {
        if (Project == null) return;
        _store.Save(Project);
        _settings.RememberProject(Project.Name, Project.RootPath);
        SetStatus("Project saved.");
    }

    public void SaveAs(string newRoot)
    {
        if (Project == null) return;
        _store.SaveAs(Project, newRoot);
        _settings.RememberProject(Project.Name, Project.RootPath);
        SetStatus("Project saved to the new folder.");
    }

    public void ImportAudio(string path)
    {
        if (Project == null) return;
        Remember();
        var target = SelectedTrack() ?? Project.Tracks.FirstOrDefault(t => t.Role == TrackRole.Backing);
        _engine.ImportIntoProject(path, target);
        SetStatus("Backing track copied into the project. The original file was left untouched.");
        Raise();
    }

    public void PlayPause()
    {
        if (Project == null) return;
        if (_engine.IsPlaying && !_engine.IsRecording)
            _engine.Stop();
        else if (!_engine.IsRecording)
            _engine.Play();
    }

    public void Record()
    {
        if (Project == null) return;
        if (_engine.IsRecording)
            _ = _engine.StopRecordAsync();
        else
            _engine.StartRecord();
    }

    public void Stop() => _engine.Stop();

    public void Undo()
    {
        if (Project == null) return;
        var next = _undo.Undo(Project);
        if (next == null) return;
        Project = next;
        _engine.AttachProject(Project);
        Raise();
    }

    public void Redo()
    {
        if (Project == null) return;
        var next = _undo.Redo(Project);
        if (next == null) return;
        Project = next;
        _engine.AttachProject(Project);
        Raise();
    }

    public void ToggleMute(Track track)
    {
        Remember();
        track.Mute = !track.Mute;
        Project?.Touch();
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void ToggleSolo(Track track)
    {
        Remember();
        track.Solo = !track.Solo;
        Project?.Touch();
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void ToggleArm(Track track)
    {
        Remember();
        var arm = !track.Armed;
        foreach (var t in Project!.Tracks)
            t.Armed = t.Id == track.Id && arm;
        Project.Touch();
        Raise();
    }

    public void Tell(string message) => SetStatus(message);

    public void SetTrackGain(Track track, double db)
    {
        track.GainDb = db;
        Project?.Touch();
        _engine.NotifyProjectChanged();
    }

    public void SetTrackPan(Track track, double pan)
    {
        track.Pan = pan;
        Project?.Touch();
        _engine.NotifyProjectChanged();
    }

    public void ApplyPreset(Track track, string name)
    {
        Remember();
        VocalPresets.Apply(track, name);
        Project?.Touch();
        _engine.NotifyProjectChanged();
        SetStatus(name + " vocal starting point applied. You can still change every knob.");
    }

    public void AddTrack()
    {
        if (Project == null) return;
        Remember();
        ProjectFactory.CreateAudioTrack(Project, "Track " + (Project.Tracks.Count + 1), TrackRole.Audio, TrackChannelLayout.Stereo);
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void SplitSelected()
    {
        if (Project == null || SelectedClipId == null) return;
        var (track, clip) = FindClip(SelectedClipId);
        if (track == null || clip == null) return;
        Remember();
        ClipEditing.Split(track, clip, _engine.PlayheadFrames);
        Project.Touch();
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void DeleteSelected()
    {
        if (Project == null || SelectedClipId == null) return;
        var (track, clip) = FindClip(SelectedClipId);
        if (track == null || clip == null) return;
        Remember();
        ClipEditing.Delete(track, clip);
        Project.Touch();
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void MoveSelected(long newStart)
    {
        if (Project == null || SelectedClipId == null) return;
        var (_, clip) = FindClip(SelectedClipId);
        if (clip == null) return;
        Remember();
        ClipEditing.Move(clip, SnapFrame(newStart));
        Project.Touch();
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void FadeSelected(long fadeIn, long fadeOut)
    {
        if (SelectedClipId == null) return;
        var (_, clip) = FindClip(SelectedClipId);
        if (clip == null) return;
        Remember();
        ClipEditing.SetFades(clip, fadeIn, fadeOut);
        Project?.Touch();
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void AuditionTake(Track track, Take take)
    {
        Remember();
        ClipEditing.AuditionTake(track, take.Id);
        Project?.Touch();
        _engine.NotifyProjectChanged();
        SetStatus("Listening to " + take.Name);
        Raise();
    }

    public void AddCompFromPlayhead(Track track, Take take, long length)
    {
        Remember();
        var start = SnapFrame(_engine.PlayheadFrames);
        ClipEditing.AddCompRegion(track, take, start, Math.Max(0, start - take.StartFrame), length);
        Project?.Touch();
        _engine.NotifyProjectChanged();
        SetStatus("Added that part to the chosen performance.");
        Raise();
    }

    public void StretchSelected(double ratio)
    {
        if (Project == null || SelectedClipId == null) return;
        var (_, clip) = FindClip(SelectedClipId);
        if (clip == null) return;
        Remember();
        clip.StretchRatio = Math.Clamp(ratio, 0.85, 1.18);
        Project.Touch();
        _engine.NotifyProjectChanged();
        SetStatus("Small timing stretch set. Listen back before you keep it.");
        Raise();
    }

    public void ToggleLoop()
    {
        if (Project == null) return;
        Project.Loop.Enabled = !Project.Loop.Enabled;
        if (Project.Loop.EndFrame <= Project.Loop.StartFrame)
            Project.Loop.EndFrame = Math.Max(Project.SampleRate * 4, Project.LengthFrames());
        Project.Touch();
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void ToggleMetronome()
    {
        _engine.MetronomeEnabled = !_engine.MetronomeEnabled;
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void GoToStart()
    {
        _engine.SetPlayhead(0);
        Raise();
    }

    public void Seek(long frame)
    {
        _engine.SetPlayhead(Math.Max(0, frame));
        Raise();
    }

    public async Task ExportAsync(string dest, ExportFormat format, ExportScope scope)
    {
        if (Project == null) return;
        var start = scope == ExportScope.SelectedRange ? Project.SelectionStartFrame : 0;
        var end = scope == ExportScope.SelectedRange ? Project.SelectionEndFrame : Project.LengthFrames();
        await Task.Run(() => _exporter.Export(Project, _engine.Cache, dest, format, scope, start, end));
        SetStatus("Song exported. The click track and any reference track were left out.");
    }

    public string? BrowseOpenProject()
    {
        var dlg = new OpenFolderDialog { Title = "Open RMS project folder" };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public string? BrowseAudio()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a WAV or MP3 backing track",
            Filter = "Audio|*.wav;*.mp3|WAV|*.wav|MP3|*.mp3"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? BrowseExport(ExportFormat format)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export song",
            Filter = format == ExportFormat.Mp3 ? "MP3|*.mp3" : "WAV|*.wav",
            FileName = (Project?.Name ?? "song") + (format == ExportFormat.Mp3 ? ".mp3" : ".wav")
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public Track? SelectedTrack() =>
        Project?.Tracks.FirstOrDefault(t => t.Id == SelectedTrackId) ?? Project?.Tracks.FirstOrDefault(t => t.Armed);

    public void Dispose()
    {
        _clock.Stop();
        _autosave.Stop();
        if (Project is { Dirty: true })
        {
            try { _store.Autosave(Project); } catch { /* shutting down */ }
        }
        _engine.Dispose();
    }

    private void OpenLoaded(ProjectDocument project)
    {
        Project = project;
        _undo.Clear();
        _store.MarkUncleanExit(project.RootPath);
        _engine.AttachProject(project);
        _settings.RememberProject(project.Name, project.RootPath);
        SelectedTrackId = project.Tracks.FirstOrDefault(t => t.Armed)?.Id;
        SetStatus("Opened " + project.Name);
        Go(StudioPlace.Arrange);
    }

    private void ApplySavedDevices()
    {
        if (SelectedInput == null && SelectedOutput == null) return;
        try { ApplyDevices(); }
        catch { /* devices may be unplugged until setup */ }
    }

    private void ApplyDevices()
    {
        _engine.Configure(new EngineConfig
        {
            InputDeviceId = SelectedInput?.Id ?? _settings.InputDeviceId,
            OutputDeviceId = SelectedOutput?.Id ?? _settings.OutputDeviceId,
            ExclusiveMode = _settings.ExclusiveMode,
            SoftwareMonitor = _settings.SoftwareMonitor,
            BufferMilliseconds = _settings.BufferMilliseconds,
            UserOffsetFrames = _settings.UserRecordingOffsetFrames
        });
        if (Project != null)
            _engine.AttachProject(Project);
        _settings.Save();
        Raise();
    }

    private void Remember()
    {
        if (Project != null)
            _undo.RememberBeforeChange(Project);
    }

    private long SnapFrame(long frame) =>
        Project == null ? frame : TimelineMath.Snap(frame, Snap, Project.SampleRate, Project.TempoBpm,
            Project.TimeSignature.Numerator, Project.TimeSignature.Denominator);

    private (Track? track, AudioClip? clip) FindClip(string id)
    {
        if (Project == null) return (null, null);
        foreach (var track in Project.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == id);
            if (clip != null) return (track, clip);
        }
        return (null, null);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
    }

    private void SetStatus(string text)
    {
        Status = text;
        Raise();
    }

    private void Raise() => Changed?.Invoke();
}
