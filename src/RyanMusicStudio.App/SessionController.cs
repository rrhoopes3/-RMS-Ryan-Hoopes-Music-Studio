using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using RyanMusicStudio.Core.Dsp;
using RyanMusicStudio.Core.Editing;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Core.Studio;
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
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _deviceRescan = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private UserSettings _settings = UserSettings.Load();

    private const string NoSongMessage = "RMS does not have a song open yet.";
    private string? _heardBeforeTake; // the take the armed track played when R was pressed
    private bool _shuttingDown;
    private Task? _testTakeTask;

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
    public bool Loading { get; private set; }
    public string? Problem { get; private set; }
    public int MelodyPen { get; private set; }

    public SessionController()
    {
        _engine.StatusChanged += s => SetStatus(s);
        _engine.DeviceLost += info =>
        {
            _devicesMissing = true; // re-apply the device when it comes back
            SetStatus(info.RecoveryMessage);
            Banner?.Invoke(info.RecoveryMessage);
        };
        _engine.Meters += m =>
        {
            InputPeak = m.InputPeak;
            OutputPeak = Math.Max(m.OutputPeakL, m.OutputPeakR);
            Clipping = m.ClipCount > 0 || m.InputPeak >= 0.98f;
        };
        // The engine commits takes on a worker thread; touch the song only on the UI thread.
        _engine.TakeCommitted += result => _ui.InvokeAsync(() => OnTakeCommitted(result));
        _engine.PlayheadMoved += frame =>
        {
            if (Project != null)
            {
                var marker = Project.MarkerAtOrBefore(frame);
                Lyrics = string.IsNullOrWhiteSpace(marker?.Lyrics) ? "" : $"{marker.Name}: {marker.Lyrics}";
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
        // Plugging a USB mic in after launch should just work. Windows sends several
        // notifications per plug, so wait for them to settle before rescanning.
        _engine.DevicesChanged += () => _ui.InvokeAsync(() =>
        {
            _deviceRescan.Stop();
            _deviceRescan.Start();
        });
        _deviceRescan.Tick += (_, _) =>
        {
            _deviceRescan.Stop();
            RescanDevices();
        };
        _engine.MetronomeEnabled = false;
        _clock.Start();
        _autosave.Start();
        RefreshDevices();
        ApplySavedDevices();
    }

    public void Go(StudioPlace place)
    {
        Place = place;
        _engine.SetInputPreview(place == StudioPlace.Setup);
        Raise();
    }

    public void RefreshDevices()
    {
        Inputs = _engine.Inputs();
        Outputs = _engine.Outputs();
        // A device the singer chose is used whenever it is plugged in. If it is unplugged, nothing is
        // picked in its place (the spec says never to switch devices silently). With no choice made
        // yet, follow the Windows default device.
        SelectedInput = Pick(Inputs, _settings.InputDeviceId, _engine.Devices.DefaultInputId());
        SelectedOutput = Pick(Outputs, _settings.OutputDeviceId, _engine.Devices.DefaultOutputId());
        Raise();
    }

    private static AudioDeviceInfo? Pick(IReadOnlyList<AudioDeviceInfo> devices, string? chosenId, string? defaultId) =>
        !string.IsNullOrEmpty(chosenId)
            ? devices.FirstOrDefault(d => d.Id == chosenId)
            : devices.FirstOrDefault(d => d.Id == defaultId) ?? devices.FirstOrDefault();

    /// <summary>Re-reads the device list and switches to the chosen devices if they changed.</summary>
    public void RescanDevices(bool fromButton = false)
    {
        if (_engine.IsTakeActive || _engine.IsPlaying || _engine.IsTestTaking)
        {
            if (fromButton)
            {
                RefreshDevices(); // the list may update now; the streams switch once playback stops
                SetStatus("Device list updated. RMS switches devices when playback or recording stops.");
            }
            _deviceRescan.Start(); // try again once the take or playback is over
            return;
        }
        RefreshDevices();
        if (ReportMissingDevices() || SelectedInput == null || SelectedOutput == null) return;
        var cameBack = _devicesMissing;
        if (!cameBack && SelectedInput.Id == _engine.Config.InputDeviceId && SelectedOutput.Id == _engine.Config.OutputDeviceId) return;
        _devicesMissing = false;
        try
        {
            ApplyDevices();
            SetStatus($"Using {SelectedInput.Name} and {SelectedOutput.Name}.");
        }
        catch (Exception ex) { SetStatus("RMS could not switch audio devices: " + ex.Message); }
    }

    // A chosen device that is unplugged is reported, never silently replaced (spec 2.1).
    private bool _devicesMissing;

    private bool ReportMissingDevices()
    {
        var missing = new List<string>();
        if (SelectedInput == null && !string.IsNullOrEmpty(_settings.InputDeviceId)) missing.Add("microphone");
        if (SelectedOutput == null && !string.IsNullOrEmpty(_settings.OutputDeviceId)) missing.Add("headphones");
        if (missing.Count == 0) return false;
        _devicesMissing = true;
        SetStatus($"Your chosen {string.Join(" and ", missing)} isn't plugged in. Plug it back in, or pick another from the Microphone or Speakers list.");
        return true;
    }

    // Changing devices or buffer reconfigures the engine, which would end a take in progress.
    private bool SettingsLockedForTake()
    {
        if (!BusyRecording("Stop recording first, then change audio settings.")) return false;
        Raise(); // put the control back to the real setting
        return true;
    }

    public void SetInputChannel(int channel)
    {
        if (SettingsLockedForTake()) return;
        _settings.InputChannel = Math.Clamp(channel, 0, 2);
        _engine.Config.InputChannel = _settings.InputChannel; // takes effect on the next mic buffer
        _settings.Save();
        Raise();
    }

    public void ChooseInput(AudioDeviceInfo device)
    {
        if (SettingsLockedForTake()) return;
        SelectedInput = device;
        _settings.InputDeviceId = device.Id;
        ApplyDevices();
    }

    public void ChooseOutput(AudioDeviceInfo device)
    {
        if (SettingsLockedForTake()) return;
        SelectedOutput = device;
        _settings.OutputDeviceId = device.Id;
        ApplyDevices();
    }

    public void SetExclusive(bool exclusive)
    {
        if (SettingsLockedForTake()) return;
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
        if (SettingsLockedForTake()) return;
        _settings.BufferMilliseconds = Math.Clamp(ms, 8, 80);
        ApplyDevices();
    }

    public void SetUserOffsetMs(double ms)
    {
        if (SettingsLockedForTake()) return;
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

    public void PlayTestTone()
    {
        if (BusyRecording("Stop recording first, then play the test tone.") || _engine.IsTestTaking) return;
        try { _engine.PlayTestTone(); }
        catch (Exception ex) { SetStatus("RMS could not play the test tone: " + ex.Message); }
        _engine.SetInputPreview(Place == StudioPlace.Setup); // the tone reopened the output and closed the mic
    }

    public Task RecordTestTakeAsync()
    {
        if (_shuttingDown || BusyRecording("Stop recording first, then record a test.") || _engine.IsTestTaking)
            return Task.CompletedTask;
        return _testTakeTask = RunTestTakeAsync();
    }

    private async Task RunTestTakeAsync()
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "RMS", "test-takes");
            SetStatus("Recording a short test. Sing a line…");
            var path = await _engine.RecordTestTakeAsync(TimeSpan.FromSeconds(4), folder);
            if (_shuttingDown || _engine.IsTakeActive) return;
            _engine.PlayFile(path);
            SetStatus("That was your test take. If you heard it, you are ready to record a song.");
        }
        catch (Exception ex)
        {
            SetStatus("RMS could not record the test: " + ex.Message + " Check the microphone choice above.");
        }
        finally
        {
            _testTakeTask = null;
            if (!_shuttingDown)
                _engine.SetInputPreview(Place == StudioPlace.Setup);
        }
    }

    /// <summary>False (and says why) while a take or count-in is running.</summary>
    public bool CanSwitchSong() =>
        !BusyRecording("Stop recording first (press R or Space), then switch songs.") &&
        !TakeSaveFailed();

    public void NewVocalSession(string name, string parentFolder, double tempo, int num, int den, int sampleRate)
    {
        if (!CanSwitchSong() || !LeaveProject()) return;
        // Never write over an existing song; the dialog suggests the same name every time.
        var requestedName = name;
        var root = Path.Combine(parentFolder, Sanitize(name));
        for (var n = 2; ProjectPaths.LooksLikeProject(root); n++)
        {
            name = $"{requestedName} ({n})";
            root = Path.Combine(parentFolder, Sanitize(name));
        }
        var project = ProjectFactory.CreateVocalOverBeat(name, root, tempo, num, den, sampleRate);
        VocalPresets.Apply(project.Tracks.First(t => t.Role == TrackRole.Vocal), VocalPresets.Clean);
        _store.Save(project);
        OpenLoaded(project);
        SetStatus(name == requestedName
            ? "New vocal session ready. Drag a backing track onto the timeline."
            : $"\"{requestedName}\" already exists, so this session is called \"{name}\". Drag a backing track onto the timeline.");
        Go(_settings.AudioSetupConfirmed ? StudioPlace.Arrange : StudioPlace.Setup);
    }

    public bool TryOpen(string root, bool useAutosave = false)
    {
        if (!CanSwitchSong()) return false;
        if (!ProjectPaths.LooksLikeProject(root))
        {
            SetStatus("That folder is not a song RMS can open.");
            return false;
        }
        if (!useAutosave && Project != null && SamePath(root, Project.RootPath))
        {
            // Already open: reloading from disk would throw away work that isn't saved yet.
            Go(StudioPlace.Arrange);
            return true;
        }

        if (!LeaveProject()) return false; // save the current song before anything is read from disk
        var offer = _store.InspectRecovery(root);
        RecoveryText = offer == null ? "" : offer.Message + " Finished takes on disk: " + offer.CompletedTakeFiles.Count + ".";
        ProjectDocument project;
        // Only an autosave newer than the last save is worth restoring, and the singer decides.
        if (useAutosave || (offer is { AutosaveIsNewer: true } && ConfirmRestore()))
        {
            project = _store.OpenAutosave(root);
            project.Touch(); // restored work isn't in project.json yet, so it still needs saving
        }
        else
            project = _store.Open(root);

        OpenLoaded(project);
        if (offer is { AutosaveIsNewer: false })
            Banner?.Invoke(RecoveryText); // a real crash: say so, and that finished takes are on disk
        return true;
    }

    public void Save()
    {
        if (Project == null) return;
        if (BusyRecording("Stop recording first, then save.") || TakeSaveFailed()) return;
        _store.Save(Project);
        _settings.RememberProject(Project.Name, Project.RootPath);
        Problem = null;
        SetStatus("Saved.");
    }

    public void SaveAs(string newRoot)
    {
        if (Project == null) return;
        if (BusyRecording("Stop recording first, then save a copy.") || TakeSaveFailed()) return;
        if (ProjectPaths.LooksLikeProject(newRoot) && !SamePath(newRoot, Project.RootPath))
        {
            SetStatus("That folder already holds an RMS song. Choose an empty folder for the copy.");
            return;
        }
        _store.SaveAs(Project, newRoot);
        _settings.RememberProject(Project.Name, Project.RootPath);
        SetStatus("Project saved to the new folder.");
    }

    public void BrowseAndImport()
    {
        if (Project == null)
        {
            SetStatus(NoSongMessage);
            return;
        }
        var path = BrowseAudio();
        if (path != null)
            ImportAudio(path);
    }

    public void ImportAudio(string path)
    {
        if (Project == null)
        {
            SetStatus(NoSongMessage);
            return;
        }
        if (BusyRecording("Stop recording first, then import.")) return;
        // Imported audio goes on its own track: a take on the same track would silence it, and two
        // files on one track would play on top of each other in one lane.
        static bool Free(Track t) => !t.Armed && t.Takes.Count == 0 && t.Clips.Count == 0;
        var selected = SelectedTrack();
        var target = selected != null && selected.Role != TrackRole.Vocal && Free(selected)
            ? selected
            : Project.Tracks.FirstOrDefault(t => t.Role == TrackRole.Backing && Free(t));
        Remember();
        var created = target == null;
        target ??= ProjectFactory.CreateAudioTrack(Project, NextTrackName("Backing track"), TrackRole.Backing, TrackChannelLayout.Stereo);
        try
        {
            _engine.ImportIntoProject(path, target);
        }
        catch (Exception ex)
        {
            if (created) Project.Tracks.Remove(target);
            SetStatus($"RMS couldn't read {Path.GetFileName(path)}: {ex.Message} Try a WAV or MP3 file.");
            return;
        }
        SetStatus($"{Path.GetFileName(path)} copied onto {target.Name}. The original file was left untouched.");
        Raise();
    }

    public void StartStudio()
    {
        Loading = true;
        Problem = null;
        SetStatus("Opening your song…");
        try
        {
            _engine.MetronomeEnabled = false;
            var opened = false;
            foreach (var recent in _settings.Recent)
            {
                if (!ProjectPaths.LooksLikeProject(recent.Path)) continue;
                opened = TryOpen(recent.Path);
                break;
            }

            if (!opened && Project == null)
            {
                var folder = DefaultSongFolder();
                Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
                if (ProjectPaths.LooksLikeProject(folder))
                    opened = TryOpen(folder);
                if (!opened && Project == null && ProjectPaths.LooksLikeProject(folder))
                {
                    Problem = "RMS could not open your song. Press Open and choose the song folder.";
                    SetStatus(Problem);
                }
                else if (!opened && Project == null)
                {
                    var project = ProjectFactory.CreateStudioSong("My Song", folder, 100, 48000);
                    VocalPresets.Apply(project.Tracks.First(t => t.Role == TrackRole.Vocal), VocalPresets.Clean);
                    StudioBedWriter.Write(project);
                    _store.Save(project);
                    OpenLoaded(project);
                }
            }

            if (Project != null && Problem == null)
            {
                EnsureStudioAudio();
                SayReady();
            }
        }
        catch (Exception ex)
        {
            Problem = "RMS could not open your song. " + ex.Message;
            SetStatus(Problem);
        }
        finally
        {
            Loading = false;
            Raise();
        }
    }

    public void OpenSongFolder(string root)
    {
        Loading = true;
        Problem = null;
        SetStatus("Opening your song…");
        try
        {
            if (!TryOpen(root))
            {
                Problem = string.IsNullOrWhiteSpace(Status)
                    ? "That folder is not a song RMS can open."
                    : Status;
                SetStatus(Problem);
                return;
            }
            EnsureStudioAudio();
            SayReady();
        }
        catch (Exception ex)
        {
            Problem = "RMS could not open that song. " + ex.Message;
            SetStatus(Problem);
        }
        finally
        {
            Loading = false;
            Raise();
        }
    }

    public void PlayFromStart()
    {
        if (Project == null)
        {
            SetStatus(Problem ?? NoSongMessage);
            return;
        }
        if (_engine.IsTakeActive)
        {
            SetStatus("You are recording. Press Stop when you are done.");
            return;
        }
        try
        {
            _engine.Stop();
            _engine.SetPlayhead(0);
            _engine.Play();
            Problem = null;
        SetStatus(StudioSong.LooksEmpty(Project)
            ? "This song is quiet. Tap the drum boxes or piano keys, then press Play."
            : "Playing.");
        }
        catch (Exception)
        {
            Problem = "RMS can't play sound yet. Plug in speakers or headphones, then press Play again.";
            SetStatus(Problem);
        }
    }

    public void StopSong()
    {
        var wasRecording = _engine.IsTakeActive;
        Stop();
        if (!wasRecording)
            _engine.SetPlayhead(0);
        if (Project != null && StudioSong.LooksEmpty(Project))
            SetStatus("Stopped. This song is quiet. Tap the drum boxes or piano keys, then press Play.");
        else if (!wasRecording)
            SetStatus("Stopped.");
    }

    public void NudgeTempo(int delta)
    {
        if (Project == null) return;
        var next = Math.Clamp(Math.Round(Project.TempoBpm + delta), 60, 180);
        if (Math.Abs(next - Project.TempoBpm) < 0.1) return;
        Project.TempoBpm = next;
        _engine.RefreshStudioBed();
        if (_engine.PlayheadFrames >= Project.Loop.EndFrame)
            _engine.SetPlayhead(0);
        SetStatus("Tempo is " + (int)next + ".");
    }

    public void SetVolume(int percent)
    {
        if (Project == null) return;
        percent = Math.Clamp(percent, 0, 100);
        if (Project.Studio.VolumePercent == percent) return;
        Project.Studio.VolumePercent = percent;
        StudioSong.ApplyVolume(Project);
        _engine.NotifyProjectChanged();
        SetStatus(percent == 0 ? "Volume is all the way down." : "Volume is " + percent + ".");
    }

    public void ToggleDrum(DrumVoice voice, int step)
    {
        if (Project == null) return;
        var on = Project.Studio.ToggleDrum(voice, step);
        _engine.RefreshStudioBed();
        var name = voice switch
        {
            DrumVoice.Kick => "Kick",
            DrumVoice.Snare => "Snare",
            _ => "Hat"
        };
        Problem = null;
        if (Project.Studio.VolumePercent == 0)
        {
            SetStatus(on
                ? name + " is on. Turn Volume up to hear it."
                : name + " is off. Volume is all the way down.");
            return;
        }
        if (!_engine.IsSongPlaying)
            _engine.PreviewSamples(StudioSynth.PreviewDrum(Project.Studio, voice, Project.SampleRate), Project.SampleRate);
        SetStatus(on ? name + " is on." : name + " is off.");
    }

    public void ChooseMelodyKey(int degree)
    {
        if (degree < 0 || degree >= SongSketch.NoteNames.Length) return;
        MelodyPen = degree;
        if (Project == null)
        {
            SetStatus(SongSketch.NoteName(degree) + " selected. Tap a melody box to place it.");
            return;
        }
        if (Project.Studio.VolumePercent == 0)
        {
            SetStatus(SongSketch.NoteName(degree) + " selected. Turn Volume up to hear it.");
            return;
        }
        if (!_engine.IsSongPlaying)
            _engine.PreviewSamples(StudioSynth.PreviewNote(Project.Studio, degree, Project.SampleRate), Project.SampleRate);
        SetStatus(SongSketch.NoteName(degree) + " selected. Tap a melody box to place it.");
    }

    public void PlaceMelody(int step)
    {
        if (Project == null) return;
        Project.Studio.SetMelody(step, MelodyPen);
        _engine.RefreshStudioBed();
        var name = SongSketch.NoteName(Project.Studio.Melody[Math.Clamp(step, 0, SongSketch.StepCount - 1)]);
        Problem = null;
        SetStatus(string.IsNullOrEmpty(name)
            ? "That melody box is empty."
            : name + " is in the melody.");
    }

    public void RecordVoice()
    {
        if (_engine.IsRecording || _engine.IsTakeActive)
        {
            Stop();
            SetStatus("Recording stopped. Press Play to hear it with the beat.");
            return;
        }
        Record();
        if (_engine.IsTakeActive)
            SetStatus("Recording. Sing or play, then press Stop.");
    }

    public int CurrentStep => Project == null
        ? 0
        : StudioSong.StepAt(Project.SampleRate, Project.TempoBpm, _engine.PlayheadFrames);

    public static string DefaultSongFolder()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrWhiteSpace(music))
            music = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(music, "RMS", "My Song");
    }

    private void PrepareStudioRecord()
    {
        if (Project == null || !StudioSong.HasBed(Project)) return;
        StudioSong.ArmBeatLoop(Project);
        _engine.NotifyProjectChanged();
        _engine.SetPlayhead(0);
    }

    private void RestoreStudioAfterRecord()
    {
        if (Project == null || !StudioSong.HasBed(Project)) return;
        _engine.RefreshStudioBed();
    }

    private void EnsureStudioAudio()
    {
        if (Project == null) return;
        var absolute = Path.Combine(Project.RootPath, StudioSong.BedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var referenced = StudioSong.HasBed(Project);
        if (!referenced && Project.Studio.IsEmpty) return;
        if (!referenced || !File.Exists(absolute))
            _engine.RefreshStudioBed();
        else
        {
            StudioSong.ApplyVolume(Project, touch: false);
            if (StudioSong.NeedsFit(Project))
                StudioSong.Place(Project, StudioSong.BarFrames(Project.SampleRate, Project.TempoBpm));
            _engine.NotifyProjectChanged();
        }
    }

    private void SayReady()
    {
        Problem = null;
        if (Project == null)
        {
            SetStatus(NoSongMessage);
            return;
        }
        SetStatus(StudioSong.LooksEmpty(Project)
            ? "This song is quiet. Tap the drum boxes or piano keys, then press Play."
            : "Press Play to hear your song.");
    }

    public void PlayPause()
    {
        if (Project == null)
        {
            SetStatus(NoSongMessage);
            return;
        }
        if (_engine.IsRecording)
            _ = StopCurrentTakeAsync(); // Space ends the take and keeps it
        else if (_engine.IsPlaying || _engine.IsTakeActive)
            Stop();
        else
        {
            try { _engine.Play(); }
            catch (Exception ex)
            {
                _engine.Stop();
                SetStatus("RMS could not play: " + ex.Message);
            }
        }
    }

    public void Record()
    {
        if (Project == null)
        {
            SetStatus(NoSongMessage);
            return;
        }
        if (_engine.IsRecording)
        {
            _ = StopCurrentTakeAsync();
            return;
        }
        if (_engine.IsTakeActive)
        {
            var wasCountingIn = _engine.IsCountingIn;
            Stop();
            SetStatus(wasCountingIn ? "Count-in cancelled." : "Recording stopped.");
            return;
        }
        if (_engine.IsTestTaking)
        {
            SetStatus("Wait for the test take to finish, then press R.");
            return;
        }
        PrepareStudioRecord();
        var armed = Project.Tracks.FirstOrDefault(t => t.Armed) ?? Project.Tracks.FirstOrDefault(t => t.Role == TrackRole.Vocal);
        _heardBeforeTake = armed?.AuditionTakeId;
        try
        {
            _engine.StartRecord();
        }
        catch (Exception)
        {
            Stop();
            RestoreStudioAfterRecord();
            SetStatus("RMS can't hear a microphone. Plug one in, or choose it from the Microphone list, then press Record my voice again.");
            return;
        }
        // The engine adds the take when recording stops; this snapshot lets Ctrl+Z remove that
        // take (Remember() skips edits made during the take, so they undo together with it).
        _undo.RememberBeforeChange(Project);
    }

    public void Stop()
    {
        if (_engine.IsTakeActive)
            _ = StopCurrentTakeAsync();
        else
        {
            _engine.Stop();
            _engine.SetInputPreview(Place == StudioPlace.Setup);
        }
    }

    private async Task StopCurrentTakeAsync()
    {
        try
        {
            // A test take also owns the capture device, but is not a song take. Let its short
            // recording finish before disposing the engine; skip its preview during shutdown.
            if (_testTakeTask != null)
                await _testTakeTask;
            await _engine.StopRecordAsync();
        }
        catch (Exception ex)
        {
            var message = "RMS could not save the take: " + ex.Message +
                " RMS kept its recovery marker and any audio written to the song folder.";
            SetStatus(message);
            Banner?.Invoke(message);
        }
        finally
        {
            if (!_shuttingDown)
            {
                _engine.SetInputPreview(Place == StudioPlace.Setup);
                RestoreStudioAfterRecord();
            }
        }
    }

    /// <summary>Quit after a failed take without replacing the song or its recovery files.</summary>
    public void ExitPreservingRecovery()
    {
        if (!_engine.HasTakeFinalizationError)
            throw new InvalidOperationException("There is no failed take to recover.");
        _shuttingDown = true;
        _clock.Stop();
        _autosave.Stop();
        _deviceRescan.Stop();
        _engine.DisposePreservingRecovery();
    }

    public void Undo()
    {
        if (Project == null || RecordingBlocksUndo()) return;
        var next = _undo.Undo(Project);
        if (next == null) return;
        ReplaceAfterUndo(next);
    }

    public void Redo()
    {
        if (Project == null || RecordingBlocksUndo()) return;
        var next = _undo.Redo(Project);
        if (next == null) return;
        ReplaceAfterUndo(next);
    }

    private void ReplaceAfterUndo(ProjectDocument next)
    {
        var playhead = _engine.PlayheadFrames;
        Project = next;
        Project.Touch(); // the restored copy differs from the file on disk, so it still needs saving
        _engine.AttachProject(Project);
        _engine.SetPlayhead(playhead);
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

    /// <summary>Makes the selected track the one that R records into.</summary>
    public void ArmSelectedTrack()
    {
        var track = SelectedTrack();
        if (Project == null || track == null || BusyRecording("Stop recording first, then switch tracks.")) return;
        if (track.Clips.Count > 0 || track.Role == TrackRole.Backing)
        {
            // A take on a track with imported audio would replace that audio in playback.
            SetStatus($"{track.Name} is for imported audio. Use Add track to make a guitar or vocal track to record into.");
            return;
        }
        Remember();
        ArmOnly(track);
        SetStatus($"R now records into {track.Name}.");
        Raise();
    }

    /// <summary>Adds a recordable track (e.g. "Guitar") and makes it the one R records into.</summary>
    public void AddRecordingTrack(string baseName, TrackRole role)
    {
        if (Project == null)
        {
            SetStatus(NoSongMessage);
            return;
        }
        if (BusyRecording("Stop recording first, then add a track.")) return;
        Remember();
        var name = NextTrackName(baseName);
        var track = ProjectFactory.CreateAudioTrack(Project, name, role, TrackChannelLayout.Mono);
        if (role == TrackRole.Vocal)
            VocalPresets.Apply(track, VocalPresets.Clean);
        ArmOnly(track);
        SelectedTrackId = track.Id;
        _engine.NotifyProjectChanged();
        SetStatus($"{name} track added. Press R to record into it.");
        Raise();
    }

    private string NextTrackName(string baseName)
    {
        var name = baseName;
        for (var n = 2; Project!.Tracks.Any(t => t.Name == name); n++)
            name = $"{baseName} {n}";
        return name;
    }

    private void ArmOnly(Track track)
    {
        foreach (var t in Project!.Tracks)
            t.Armed = t.Id == track.Id;
        Project.Touch();
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
        SetStatus(name + " vocal starting point applied: it sets the EQ, compression and reverb for you.");
    }

    public void SplitSelected()
    {
        var (track, clip) = SelectedClipOrExplain();
        if (Project == null || track == null || clip == null) return;
        Remember();
        ClipEditing.Split(track, clip, _engine.PlayheadFrames);
        Project.Touch();
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void DeleteSelected()
    {
        if (Project == null || SelectedClipId == null) return;
        if (FindTake(SelectedClipId) is { } take)
        {
            if (BusyRecording("Stop recording first, then remove a take.")) return;
            var owner = Project.Tracks.First(t => t.Takes.Contains(take));
            Remember();
            var removed = owner.Comp.Regions.Where(r => r.TakeId == take.Id).ToList();
            owner.Takes.Remove(take);
            owner.Comp.Regions.RemoveAll(r => r.TakeId == take.Id);
            if (owner.AuditionTakeId == take.Id)
                owner.AuditionTakeId = null;
            if (owner.Comp.Regions.Count > 0)
            {
                // Fill the deleted take's chosen parts with the take you hear, so nothing goes silent.
                var fallback = owner.Takes.FirstOrDefault(t => t.Id == owner.AuditionTakeId)
                               ?? owner.Takes.Where(t => t.Committed).OrderBy(t => t.RecordedUtc).LastOrDefault();
                if (fallback != null)
                    foreach (var r in removed)
                        ClipEditing.ChoosePart(owner, fallback, r.TimelineStartFrame, r.EndFrame, Seam);
                if (owner.Comp.Regions.Select(r => r.TakeId).Distinct().Count() == 1)
                {
                    // Only one take left in the chosen parts: just play that take.
                    owner.AuditionTakeId = owner.Comp.Regions[0].TakeId;
                    owner.Comp.Regions.Clear();
                }
            }
            SelectedClipId = null;
            Project.Touch();
            _engine.NotifyProjectChanged();
            SetStatus($"{take.Name} removed. Ctrl+Z brings it back; the recording also stays in the song folder.");
            Raise();
            return;
        }
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
        var (_, clip) = SelectedClipOrExplain();
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
        SetStatus(track.Comp.Regions.Count > 0
            ? $"{take.Name} is marked, but you're hearing your chosen parts. Use Clear chosen parts to listen to whole takes."
            : "Listening to " + take.Name);
        Raise();
    }

    /// <summary>
    /// Uses [start, end) of a take as the performance there ("Choose best parts"), replacing
    /// earlier choices in that range so two takes never play on top of each other.
    /// </summary>
    public void ChoosePart(string takeId, long start, long end)
    {
        if (Project == null) return;
        var track = Project.Tracks.FirstOrDefault(t => t.Takes.Any(k => k.Id == takeId));
        var take = track?.Takes.First(k => k.Id == takeId);
        if (track == null || take == null) return;
        start = Math.Max(Math.Min(start, end), take.StartFrame);
        end = Math.Min(Math.Max(start, end), take.StartFrame + take.LengthFrames);
        if (end - start < Project.SampleRate / 10) return; // a click, not a drag
        Remember();
        ClipEditing.SeedComp(track, track.AuditionTakeId);
        ClipEditing.ChoosePart(track, take, start, end, Seam);
        Project.Touch();
        _engine.NotifyProjectChanged();
        SetStatus($"Using {take.Name} from {Clock(start)} to {Clock(end)}. Press Space to hear your chosen parts.");
        Raise();
    }

    public void ClearChosenParts()
    {
        if (Project == null) return;
        var track = SelectedTrack() is { Comp.Regions.Count: > 0 } selected
            ? selected
            : Project.Tracks.FirstOrDefault(t => t.Comp.Regions.Count > 0);
        if (track == null)
        {
            SetStatus("No chosen parts yet. Turn on Choose best parts, then drag across the best part of a take.");
            return;
        }
        Remember();
        track.Comp.Regions.Clear();
        Project.Touch();
        _engine.NotifyProjectChanged();
        SetStatus($"Chosen parts on {track.Name} cleared. You're hearing whole takes again.");
        Raise();
    }

    private long Seam => (Project?.SampleRate ?? 48000) / 100; // 10 ms crossfade at every join

    // A punch-in or a loop-recording pass replaces only its own range; the rest of the track keeps
    // the take the singer was hearing when they pressed R.
    private void OnTakeCommitted(RecordedTakeResult result)
    {
        var take = result.Take;
        var track = Project?.Tracks.FirstOrDefault(t => t.Takes.Contains(take));
        if (Project == null || track == null) return;
        if (StudioSong.HasBed(Project))
        {
            RestoreStudioAfterRecord();
            SetStatus(take.Name + " is saved. Press Play to hear it with the beat.");
            Raise();
            return;
        }
        var looping = Project.Loop.Enabled && Project.Loop.EndFrame > Project.Loop.StartFrame;
        var partial = Project.Punch.Enabled || looping;
        if (track.Takes.Count > 1 && (partial || track.Comp.Regions.Count > 0))
        {
            if (partial)
                ClipEditing.SeedComp(track, _heardBeforeTake, exclude: take);
            if (track.Comp.Regions.Count > 0)
            {
                var takeEnd = take.StartFrame + take.LengthFrames;
                var start = Project.Punch.Enabled ? Project.Punch.StartFrame : take.StartFrame;
                var end = Project.Punch.Enabled ? Project.Punch.EndFrame
                    : looping ? Math.Min(takeEnd, Project.Loop.EndFrame) : takeEnd;
                ClipEditing.ChoosePart(track, take, start, end, Seam);
                Project.Touch();
                _engine.NotifyProjectChanged();
                SetStatus($"{take.Name} replaces {Clock(start)} to {Clock(end)}; the rest of {track.Name} is unchanged.");
            }
        }
        Raise();
    }

    public bool HasSelection => Project is { } p && p.SelectionEndFrame > p.SelectionStartFrame;

    /// <summary>The range dragged on the ruler; loop, punch-in and "Selected range" export use it.</summary>
    public void SetSelection(long start, long end)
    {
        if (Project == null || BusyRecording("Stop recording first, then mark a range.")) return;
        if (start == end && (Project.Punch.Enabled || Project.Loop.Enabled))
            return; // a plain click on the ruler just moves the playhead; punch/loop keep their range
        Project.SelectionStartFrame = Math.Max(0, Math.Min(start, end));
        Project.SelectionEndFrame = Math.Max(0, Math.Max(start, end));
        if (HasSelection)
            SetStatus($"Selected {Clock(Project.SelectionStartFrame)} to {Clock(Project.SelectionEndFrame)}. Loop, punch-in and export can use this range.");

        // Loop and punch follow the marked range, so what's drawn is what plays and records.
        if (Project.Punch.Enabled)
        {
            if (HasSelection)
            {
                Project.Punch.StartFrame = Project.SelectionStartFrame;
                Project.Punch.EndFrame = Project.SelectionEndFrame;
            }
            else
            {
                Project.Punch.Enabled = false;
                SetStatus("Punch-in is off because no range is marked.");
            }
            Project.Touch();
        }
        if (Project.Loop.Enabled && HasSelection)
        {
            Project.Loop.StartFrame = Project.SelectionStartFrame;
            Project.Loop.EndFrame = Project.SelectionEndFrame;
            Project.Touch();
        }
        _engine.NotifyProjectChanged();
        Raise();
    }

    public void SetPunch(bool on)
    {
        if (Project == null) return;
        if (BusyRecording("Stop recording first, then change punch-in."))
        {
            Raise();
            return;
        }
        if (on && !HasSelection)
        {
            Project.Punch.Enabled = false;
            SetStatus("First drag on the ruler above the tracks to mark the part to re-record, then tick Punch in/out.");
            Raise();
            return;
        }
        Project.Punch.Enabled = on;
        if (on)
        {
            Project.Punch.StartFrame = Project.SelectionStartFrame;
            Project.Punch.EndFrame = Project.SelectionEndFrame;
            SetStatus($"Punch-in ready: R plays a bar before {Clock(Project.Punch.StartFrame)}, then records until {Clock(Project.Punch.EndFrame)}.");
        }
        Project.Touch();
        _engine.NotifyProjectChanged();
        Raise();
    }

    /// <summary>Moves the selected take or clip 20 ms later (+1) or earlier (-1) without changing its pitch.</summary>
    public void NudgeSelected(int direction)
    {
        if (Project == null) return;
        var step = direction * (long)(Project.SampleRate / 50);
        var take = SelectedClipId == null ? null : FindTake(SelectedClipId);
        var clip = take == null ? SelectedClipOrExplain().clip : null;
        if (take == null && clip == null) return;
        Remember();
        if (take != null)
        {
            step = Math.Max(step, -take.StartFrame);
            take.StartFrame += step;
            // Chosen parts keep their place and their crossfades; the take's audio slides inside them.
            foreach (var region in Project.Tracks.SelectMany(t => t.Comp.Regions).Where(r => r.TakeId == take.Id))
                region.SourceOffsetFrames = Math.Clamp(region.SourceOffsetFrames - step, take.SourceOffsetFrames,
                    Math.Max(take.SourceOffsetFrames, take.SourceOffsetFrames + take.LengthFrames - region.LengthFrames));
        }
        else
            ClipEditing.Move(clip!, clip!.StartFrame + step);
        Project.Touch();
        _engine.NotifyProjectChanged();
        SetStatus(direction > 0 ? "Moved 20 ms later. Listen back before you keep it." : "Moved 20 ms earlier. Listen back before you keep it.");
        Raise();
    }

    public void ToggleLoop()
    {
        if (Project == null) return;
        if (BusyRecording("Stop recording first, then change the loop."))
        {
            Raise();
            return;
        }
        Project.Loop.Enabled = !Project.Loop.Enabled;
        if (Project.Loop.Enabled && HasSelection)
        {
            // Loop the part the singer marked, e.g. to practise or record a tricky line over and over.
            Project.Loop.StartFrame = Project.SelectionStartFrame;
            Project.Loop.EndFrame = Project.SelectionEndFrame;
        }
        else if (Project.Loop.EndFrame <= Project.Loop.StartFrame)
            Project.Loop.EndFrame = Math.Max(Project.SampleRate * 4, Project.LengthFrames());
        if (Project.Loop.Enabled)
            SetStatus($"Looping {Clock(Project.Loop.StartFrame)} to {Clock(Project.Loop.EndFrame)}.");
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

    // Moving the backing track mid-take would put the rest of the take out of time.
    public void GoToStart()
    {
        if (BusyRecording("Stop recording first (press R or Space).")) return;
        _engine.SetPlayhead(0);
        Raise();
    }

    public void Seek(long frame)
    {
        if (BusyRecording("Stop recording first (press R or Space).")) return;
        _engine.SetPlayhead(Math.Max(0, frame));
        Raise();
    }

    public async Task ExportAsync(string dest, ExportFormat format, ExportScope scope)
    {
        if (Project == null) return;
        var start = scope == ExportScope.SelectedRange ? Project.SelectionStartFrame : 0;
        var end = scope == ExportScope.SelectedRange ? Project.SelectionEndFrame : Project.LengthFrames();
        SetStatus("Exporting " + Path.GetFileName(dest) + "…");
        await Task.Run(() => _exporter.Export(Project, _engine.Cache, dest, format, scope, start, end));
        SetStatus($"Exported {Path.GetFileName(dest)}. The click track and any reference track were left out.");
    }

    public string? BrowseOpenProject(string title = "Open a song")
    {
        var dlg = new OpenFolderDialog { Title = title };
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

    /// <summary>Finish the capture and its UI comp edits before writing the last project save.</summary>
    public async Task ShutdownAsync()
    {
        _shuttingDown = true;
        try
        {
            var testTake = _testTakeTask;
            if (testTake != null)
                await testTake;
            await _engine.StopRecordAsync();
            // TakeCommitted posts comp and punch edits to the dispatcher. This operation is queued
            // after them, so the final project save includes those edits as well as the raw take.
            await _ui.InvokeAsync(() => { });
            if (_engine.HasTakeFinalizationError)
                throw new InvalidOperationException("A take could not be finalized. RMS kept its recovery marker; the project was not saved over it.");
            Save();
            Dispose();
        }
        catch
        {
            _shuttingDown = false;
            throw;
        }
    }

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
        if (_engine.IsPlaying)
            _engine.Stop(); // the old song's mixer must not keep playing into the new one
        Project = project;
        _undo.Clear();
        try { _store.MarkUncleanExit(project.RootPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* read-only folder: no crash marker */ }
        _engine.AttachProject(project);
        _settings.RememberProject(project.Name, project.RootPath);
        SelectedTrackId = project.Tracks.FirstOrDefault(t => t.Armed)?.Id;
        SetStatus("Opened " + project.Name);
        Go(StudioPlace.Arrange);
    }

    // Switching songs works like closing the app: unsaved work is saved (Save also clears the
    // unclean-exit marker). If saving fails, the autosave keeps the newest state for next time.
    private bool LeaveProject()
    {
        if (Project == null) return true;
        if (TakeSaveFailed()) return false;
        try
        {
            if (Project.Dirty)
                _store.Save(Project);
            else
                ProjectStore.ClearCrashMarker(new ProjectPaths(Project.RootPath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                _store.Autosave(Project);
                return true;
            }
            catch (Exception) when (Project.Dirty)
            {
                // Keep the song open rather than lose work that couldn't be written anywhere.
                SetStatus($"RMS couldn't save {Project.Name}: {ex.Message} Reconnect its drive or free some space, then try again.");
                return false;
            }
            catch (Exception)
            {
                return true; // nothing unsaved; only the crash marker couldn't be cleared
            }
        }
    }

    private static bool ConfirmRestore() =>
        MessageBox.Show(
            "This song has autosaved work that is newer than its last save. RMS may not have closed cleanly.\n\n" +
            "Restore the newer work?\n\nYes: restore it.\nNo: open the last save.",
            "RMS — recover work?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private void ApplySavedDevices()
    {
        var anyChosen = !string.IsNullOrEmpty(_settings.InputDeviceId) || !string.IsNullOrEmpty(_settings.OutputDeviceId);
        if (SelectedInput == null && SelectedOutput == null && !anyChosen) return;
        // Configure even when a chosen device is missing, so the engine knows not to use another one.
        try { ApplyDevices(); }
        catch { /* devices may be unplugged until setup */ }
        ReportMissingDevices();
    }

    private void ApplyDevices()
    {
        var playhead = _engine.PlayheadFrames; // switching devices shouldn't send the song back to 0:00
        _engine.Configure(new EngineConfig
        {
            InputDeviceId = SelectedInput?.Id ?? _settings.InputDeviceId,
            OutputDeviceId = SelectedOutput?.Id ?? _settings.OutputDeviceId,
            ExclusiveMode = _settings.ExclusiveMode,
            SoftwareMonitor = _settings.SoftwareMonitor,
            BufferMilliseconds = _settings.BufferMilliseconds,
            UserOffsetFrames = _settings.UserRecordingOffsetFrames,
            InputChannel = _settings.InputChannel
        });
        if (Project != null)
        {
            _engine.AttachProject(Project);
            _engine.SetPlayhead(playhead);
        }
        if (Place == StudioPlace.Setup)
            _engine.SetInputPreview(true); // Configure closed the mic; keep the setup meter live
        _settings.Save();
        Raise();
    }

    private void Remember()
    {
        // During a take the snapshot from Record() already covers these edits.
        if (Project != null && !_engine.IsRecording && !_engine.IsCountingIn)
            _undo.RememberBeforeChange(Project);
    }

    // Swapping the document mid-take would commit the take to a detached track.
    private bool RecordingBlocksUndo() => BusyRecording("Stop recording first, then undo.");

    private bool BusyRecording(string message)
    {
        if (!_engine.IsTakeActive) return false;
        SetStatus(message);
        return true;
    }

    private bool TakeSaveFailed()
    {
        if (!_engine.HasTakeFinalizationError) return false;
        SetStatus("The last take could not be saved. Its recovery marker is still on disk. Reopen RMS to recover the audio.");
        return true;
    }

    private string Clock(long frame) => TimelineMath.FormatClock(frame, Project?.SampleRate ?? 48000);

    private long SnapFrame(long frame) =>
        Project == null ? frame : TimelineMath.Snap(frame, Snap, Project.SampleRate, Project.TempoBpm,
            Project.TimeSignature.Numerator, Project.TimeSignature.Denominator);

    private (Track? track, AudioClip? clip) SelectedClipOrExplain()
    {
        if (Project == null) return (null, null);
        if (SelectedClipId != null)
        {
            var found = FindClip(SelectedClipId);
            if (found.clip != null) return found;
            if (FindTake(SelectedClipId) != null)
            {
                SetStatus("Takes can't be split or faded yet. Use Choose best parts to keep the sections you like.");
                return (null, null);
            }
        }
        SetStatus("Click a clip on the timeline first.");
        return (null, null);
    }

    private Take? FindTake(string id) => Project?.FindTake(id);

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
