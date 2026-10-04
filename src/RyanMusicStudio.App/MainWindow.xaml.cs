using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Studio;
using RyanMusicStudio.Core.Timeline;
using RyanMusicStudio.Engine.Devices;

namespace RyanMusicStudio.App;

public partial class MainWindow : Window
{
    // Graphite palette (see App.xaml; the "Copper" brushes there are now the green accent): pads are dark until tapped.
    private static readonly SolidColorBrush PadOff = Freeze(new SolidColorBrush(Color.FromRgb(38, 42, 49)));
    private static readonly SolidColorBrush PadOn = Freeze(new SolidColorBrush(Color.FromRgb(31, 145, 80)));
    private static readonly SolidColorBrush Ink = Freeze(new SolidColorBrush(Color.FromRgb(232, 236, 241)));
    private static readonly SolidColorBrush Paper = Freeze(new SolidColorBrush(Color.FromRgb(18, 20, 23)));
    private static readonly SolidColorBrush Line = Freeze(new SolidColorBrush(Color.FromRgb(58, 64, 74)));
    private static readonly SolidColorBrush Muted = Freeze(new SolidColorBrush(Color.FromRgb(154, 163, 174)));
    private static readonly SolidColorBrush LampOn = Freeze(new SolidColorBrush(Color.FromRgb(230, 184, 77)));
    private static readonly SolidColorBrush LampOff = Freeze(new SolidColorBrush(Color.FromRgb(42, 47, 55)));
    private static readonly SolidColorBrush Recording = Freeze(new SolidColorBrush(Color.FromRgb(229, 72, 77)));
    private static readonly SolidColorBrush RecordIdle = Freeze(new SolidColorBrush(Color.FromRgb(58, 30, 33)));

    private readonly SessionController _session = new();
    private readonly Button[,] _drums = new Button[3, SongSketch.StepCount];
    private readonly Button[] _notes = new Button[SongSketch.StepCount];
    private readonly Button[] _keys = new Button[SongSketch.NoteNames.Length];
    private readonly Border[] _lamps = new Border[SongSketch.StepCount];
    private bool _syncing;
    private bool _built;
    private int _refreshQueued;
    private bool _closePending;
    private bool _closeReady;
    private bool _exporting;

    public MainWindow()
    {
        _syncing = true;
        InitializeComponent();
        BuildPads();
        Timeline.Seek = _session.Seek;
        Timeline.SelectTrack = id => _session.SelectTrack(id);
        Timeline.SelectClip = id => { _session.SelectedClipId = id; Refresh(); };
        Timeline.MoveClip = (id, frame) => { _session.SelectedClipId = id; _session.MoveSelected(frame); };
        Timeline.SelectRange = _session.SetSelection;
        Timeline.ChoosePart = _session.ChoosePart;
        Timeline.Zoomed = zoom => { _session.Zoom = zoom; Refresh(); };
        Mixer.SelectTrack = track => _session.SelectTrack(track.Id);
        Mixer.ToggleMute = _session.ToggleMute;
        Mixer.ToggleSolo = _session.ToggleSolo;
        Mixer.SetTrackGain = _session.SetTrackGain;
        Mixer.SetTrackPan = _session.SetTrackPan;
        Mixer.ApplyPreset = _session.ApplyPreset;
        Mixer.BeginMixGesture = _session.BeginMixGesture;
        Mixer.EndMixGesture = _session.EndMixGesture;
        Mixer.ReadTrackLevel = _session.TrackPeak;
        _session.Changed += QueueRefresh;
        _session.Banner += msg => Dispatcher.InvokeAsync(() =>
            MessageBox.Show(msg, "RMS", MessageBoxButton.OK, MessageBoxImage.Information));
        _syncing = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Refresh();
        Dispatcher.InvokeAsync(() =>
        {
            _session.StartStudio();
            Refresh();
        });
    }

    private void BuildPads()
    {
        if (_built) return;
        _built = true;
        var stepStyle = (Style)FindResource("StepButton");
        var keyStyle = (Style)FindResource("KeyButton");

        DrumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        for (var step = 0; step < SongSketch.StepCount; step++)
            DrumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var row = 0; row < 4; row++)
            DrumGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var step = 0; step < SongSketch.StepCount; step++)
        {
            var lamp = new Border
            {
                Height = 8,
                Margin = new Thickness(6, 0, 6, 6),
                CornerRadius = new CornerRadius(4),
                Background = LampOff
            };
            Grid.SetRow(lamp, 0);
            Grid.SetColumn(lamp, step + 1);
            DrumGrid.Children.Add(lamp);
            _lamps[step] = lamp;
        }

        AddRowLabel(DrumGrid, 1, "Kick");
        AddRowLabel(DrumGrid, 2, "Snare");
        AddRowLabel(DrumGrid, 3, "Hat");
        for (var row = 0; row < 3; row++)
        {
            for (var step = 0; step < SongSketch.StepCount; step++)
            {
                var capturedRow = row;
                var capturedStep = step;
                var button = new Button
                {
                    Style = stepStyle
                };
                button.Click += (_, _) => _session.ToggleDrum((DrumVoice)capturedRow, capturedStep);
                Grid.SetRow(button, row + 1);
                Grid.SetColumn(button, step + 1);
                DrumGrid.Children.Add(button);
                _drums[row, step] = button;
            }
        }

        for (var i = 0; i < SongSketch.NoteNames.Length; i++)
        {
            var degree = i;
            var key = new Button
            {
                Content = SongSketch.NoteNames[i],
                Style = keyStyle
            };
            AutomationProperties.SetName(key, "Piano " + SongSketch.NoteNames[i]);
            key.Click += (_, _) => _session.ChooseMelodyKey(degree);
            PianoRow.Children.Add(key);
            _keys[i] = key;
        }

        MelodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        for (var step = 0; step < SongSketch.StepCount; step++)
            MelodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        MelodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var melodyLabel = new TextBlock
        {
            Text = "Notes",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            Foreground = Muted
        };
        Grid.SetColumn(melodyLabel, 0);
        MelodyGrid.Children.Add(melodyLabel);
        for (var step = 0; step < SongSketch.StepCount; step++)
        {
            var captured = step;
            var button = new Button { Style = stepStyle };
            AutomationProperties.SetName(button, "Melody step " + (captured + 1));
            button.Click += (_, _) => _session.PlaceMelody(captured);
            Grid.SetColumn(button, step + 1);
            MelodyGrid.Children.Add(button);
            _notes[step] = button;
        }

        for (var row = 0; row < 3; row++)
        {
            for (var step = 0; step < SongSketch.StepCount; step++)
            {
                var voice = row == 0 ? "Kick" : row == 1 ? "Snare" : "Hat";
                AutomationProperties.SetName(_drums[row, step], voice + " step " + (step + 1));
            }
        }
    }

    private static void AddRowLabel(Grid grid, int row, string text)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            Foreground = Muted
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        Dispatcher.InvokeAsync(() =>
        {
            Volatile.Write(ref _refreshQueued, 0);
            Refresh();
        }, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        _syncing = true;
        var project = _session.Project;
        var busy = _session.Loading || _exporting;
        StatusText.Text = _session.Status;
        SongName.Text = project == null ? "No song yet" : project.Name;
        Title = project == null ? "RMS" : project.Name + (project.Dirty ? " •" : "") + " — RMS";
        TempoText.Text = project == null ? "100" : ((int)Math.Round(project.TempoBpm)).ToString();
        var step = _session.CurrentStep + 1;
        var clock = project == null
            ? "00:00.000"
            : TimelineMath.FormatClock(_session.Engine.PlayheadFrames, project.SampleRate);
        ClockText.Text = clock;
        StepRun.Text = "Step " + step + " / 16";

        if (project != null && (int)VolumeSlider.Value != project.Studio.VolumePercent)
            VolumeSlider.Value = project.Studio.VolumePercent;
        VolumeText.Text = ((int)VolumeSlider.Value).ToString();

        var showBanner = busy || _session.Problem != null;
        Banner.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
        BannerText.Text = busy
            ? (_exporting ? "Exporting audio…" : "Opening your song…")
            : _session.Problem ?? "";
        EmptyText.Visibility = !busy && project != null && StudioSong.LooksEmpty(project) && _session.Problem == null
            ? Visibility.Visible
            : Visibility.Collapsed;
        FillDeviceBox(MicBox, _session.Inputs, _session.SelectedInput?.Id);
        FillDeviceBox(SpeakerBox, _session.Outputs, _session.SelectedOutput?.Id);
        MicBox.IsEnabled = !busy;
        SpeakerBox.IsEnabled = !busy;

        var enabled = !busy && project != null;
        PlayButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled;
        OpenButton.IsEnabled = !busy;
        VolumeSlider.IsEnabled = enabled;

        RecordButton.Background = _session.Engine.IsRecording ? Recording : RecordIdle;

        var activeTake = _session.Engine.IsTakeActive;
        InputMeter.Level = _session.InputPeak;
        InputMeter.IsClipping = _session.InputPeak >= 0.99f;
        OutputMeter.Level = _session.OutputPeak;
        OutputMeter.IsClipping = _session.OutputPeak >= 0.99f;
        RecordingState.Text = _session.Engine.IsCountingIn ? "Count-in · get ready"
            : _session.Engine.IsRecording ? "● Recording" : activeTake ? "Saving take…" : "";
        RecordingState.Foreground = _session.Engine.IsCountingIn ? LampOn : Recording;
        var armed = project?.Tracks.FirstOrDefault(t => t.Armed);
        ArmedRun.Text = armed == null ? "No track armed" : "● " + armed.Name + " armed";
        ArmedRun.Foreground = armed == null ? Muted : Recording;
        RecDot.Visibility = activeTake ? Visibility.Collapsed : Visibility.Visible;
        RecSquare.Visibility = activeTake ? Visibility.Visible : Visibility.Collapsed;
        RecordButton.ToolTip = activeTake ? "Stop recording (R)" : "Record onto the armed track (R)";
        AutomationProperties.SetName(RecordButton, activeTake ? "Stop recording" : "Record armed track");
        RecordButton.IsEnabled = enabled && (armed != null || activeTake);
        MicBox.IsEnabled = SpeakerBox.IsEnabled = !busy && !activeTake;
        NewButton.IsEnabled = OpenButton.IsEnabled = !busy && !activeTake;
        SaveAsButton.IsEnabled = enabled && !activeTake;
        SaveButton.IsEnabled = enabled && !activeTake;
        TrackTools.IsEnabled = EditTools.IsEnabled = enabled && !activeTake;
        ExportTools.IsEnabled = enabled && !activeTake;
        Mixer.IsEnabled = enabled && !activeTake;
        Timeline.IsEnabled = enabled && !activeTake;
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;
        // Switches light up copper when on (SwitchButton style); the label stays put.
        LoopButton.Tag = project?.Loop.Enabled == true ? "on" : null;
        PunchButton.Tag = project?.Punch.Enabled == true ? "on" : null;
        CompButton.Tag = _session.CompMode ? "on" : null;
        ClickButton.Tag = _session.Engine.MetronomeEnabled ? "on" : null;
        AutomationProperties.SetItemStatus(LoopButton, LoopButton.Tag == null ? "off" : "on");
        AutomationProperties.SetItemStatus(PunchButton, PunchButton.Tag == null ? "off" : "on");
        AutomationProperties.SetItemStatus(CompButton, CompButton.Tag == null ? "off" : "on");
        AutomationProperties.SetItemStatus(ClickButton, ClickButton.Tag == null ? "off" : "on");
        Timeline.Project = project;
        Timeline.SelectedClipId = _session.SelectedClipId;
        Timeline.SelectedTrackId = _session.SelectedTrackId;
        Timeline.CompMode = _session.CompMode;
        Timeline.PixelsPerSecond = _session.Zoom;
        Timeline.Playhead = _session.Engine.PlayheadFrames;
        Timeline.LiveRecording = _session.Engine.IsRecording ? _session.Engine.GetRecordingWaveform() : null;
        Timeline.InvalidateProject();
        Mixer.SetProject(project, _session.SelectedTrackId);
        var playing = _session.Engine.IsSongPlaying;
        PlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < SongSketch.StepCount; i++)
            _lamps[i].Background = project != null && playing && i == _session.CurrentStep ? LampOn : LampOff;

        for (var row = 0; row < 3; row++)
        {
            var voice = (DrumVoice)row;
            var onRow = project?.Studio.Drums(voice);
            for (var s = 0; s < SongSketch.StepCount; s++)
            {
                var button = _drums[row, s];
                button.IsEnabled = enabled;
                var on = onRow != null && s < onRow.Count && onRow[s];
                button.Background = on ? PadOn : PadOff;
                button.Foreground = Ink;
                button.BorderBrush = s == _session.CurrentStep && playing ? LampOn : Line;
                button.BorderThickness = s == _session.CurrentStep && playing ? new Thickness(3) : new Thickness(1);
            }
        }

        for (var i = 0; i < _keys.Length; i++)
        {
            var selected = i == _session.MelodyPen;
            _keys[i].IsEnabled = enabled;
            _keys[i].Background = selected ? PadOn : Paper;
            _keys[i].Foreground = Ink;
            _keys[i].BorderBrush = selected ? PadOn : Line;
        }

        for (var s = 0; s < SongSketch.StepCount; s++)
        {
            var degree = project != null && s < project.Studio.Melody.Count ? project.Studio.Melody[s] : -1;
            var button = _notes[s];
            button.IsEnabled = enabled;
            button.Content = SongSketch.NoteName(degree);
            var on = degree >= 0;
            button.Background = on ? PadOn : PadOff;
            button.Foreground = Ink;
            button.BorderBrush = s == _session.CurrentStep && playing ? LampOn : Line;
            button.BorderThickness = s == _session.CurrentStep && playing ? new Thickness(3) : new Thickness(1);
        }

        _syncing = false;
    }

    private void Play_Click(object sender, RoutedEventArgs e) => _session.PlayPause();
    private void Stop_Click(object sender, RoutedEventArgs e) => _session.StopSong();
    private void TempoDown_Click(object sender, RoutedEventArgs e) => _session.NudgeTempo(-4);
    private void TempoUp_Click(object sender, RoutedEventArgs e) => _session.NudgeTempo(4);
    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Engine.IsTakeActive) _session.StopSong();
        else _session.Record();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => _session.GoToStart();
    private void Metronome_Click(object sender, RoutedEventArgs e) => _session.ToggleMetronome();
    private void Arm_Click(object sender, RoutedEventArgs e) => _session.ArmSelectedTrack();
    private void Import_Click(object sender, RoutedEventArgs e) => _session.BrowseAndImport();
    private void Undo_Click(object sender, RoutedEventArgs e) => _session.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _session.Redo();
    private void Split_Click(object sender, RoutedEventArgs e) => _session.SplitSelected();
    private void Delete_Click(object sender, RoutedEventArgs e) => _session.DeleteSelected();
    private void Loop_Click(object sender, RoutedEventArgs e) => _session.ToggleLoop();
    private void Punch_Click(object sender, RoutedEventArgs e) => _session.SetPunch(_session.Project?.Punch.Enabled != true);
    private void Comp_Click(object sender, RoutedEventArgs e) { _session.CompMode = !_session.CompMode; Refresh(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _session.Zoom = Math.Max(8, _session.Zoom / 1.4); Refresh(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _session.Zoom = Math.Min(800, _session.Zoom * 1.4); Refresh(); }

    private void AddTrack_Click(object sender, RoutedEventArgs e)
    {
        var name = TrackNameBox.Text.Trim();
        if (name.Length == 0) name = TrackTypeBox.SelectedIndex == 0 ? "Voice" : TrackTypeBox.SelectedIndex == 1 ? "Guitar" : "Audio";
        _session.AddRecordingTrack(name, TrackTypeBox.SelectedIndex == 0 ? TrackRole.Vocal : TrackRole.Audio);
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.CanSwitchSong()) return;
        var dialog = new NewProjectWindow(null) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { _session.NewStudioSong(dialog.ProjectName, dialog.Folder, dialog.Tempo, dialog.Numerator, dialog.Denominator, dialog.SampleRate); }
        catch (Exception ex) { MessageBox.Show(this, "Could not create the song: " + ex.Message, "RMS", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var folder = _session.BrowseOpenProject("Choose an empty folder for a copy of this song");
        if (folder == null) return;
        try
        {
            if (Directory.EnumerateFileSystemEntries(folder).Any())
            {
                MessageBox.Show(this, "Choose an empty folder for this copy so existing files are not replaced.", "RMS");
                return;
            }
            _session.SaveAs(folder);
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not save a copy: " + ex.Message, "RMS", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_exporting || _session.Project == null || _session.Engine.IsTakeActive) return;
        var format = ExportFormatBox.SelectedIndex switch { 1 => ExportFormat.Wav24, 2 => ExportFormat.Mp3, _ => ExportFormat.Wav16 };
        var scope = ExportScopeBox.SelectedIndex switch { 1 => ExportScope.SelectedRange, 2 => ExportScope.VocalStem, 3 => ExportScope.BackingStem, _ => ExportScope.WholeProject };
        if (scope == ExportScope.SelectedRange && !_session.HasSelection)
        {
            _session.Tell("Drag across the timeline ruler to select the part you want to export.");
            return;
        }
        var destination = _session.BrowseExport(format);
        if (destination == null) return;
        _session.StopSong();
        _exporting = true;
        ExportProgress.Visibility = Visibility.Visible;
        Refresh();
        try { await _session.ExportAsync(destination, format, scope); }
        catch (Exception ex)
        {
            _session.Tell("Export failed: " + ex.Message);
            MessageBox.Show(this, "Could not export audio: " + ex.Message, "RMS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _exporting = false; ExportProgress.Visibility = Visibility.Collapsed; Refresh(); }
    }

    private void Audio_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_exporting && !_session.Loading && !_session.Engine.IsTakeActive &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
            paths.Any(IsAudioFile) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Audio_Drop(object sender, DragEventArgs e)
    {
        if (_exporting || _session.Loading || _session.Engine.IsTakeActive) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            foreach (var path in paths.Where(IsAudioFile)) _session.ImportAudio(path);
        e.Handled = true;
    }

    private static bool IsAudioFile(string path) =>
        string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase);

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        _session.SetVolume((int)Math.Round(e.NewValue));
    }

    private void Save_Click(object sender, RoutedEventArgs e) => _session.Save();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var folder = _session.BrowseOpenProject();
        if (folder != null)
            _session.OpenSongFolder(folder);
    }

    private void Mic_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || MicBox.SelectedValue is not string id) return;
        var device = _session.Inputs.FirstOrDefault(d => d.Id == id);
        if (device != null)
            _session.ChooseInput(device);
    }

    private void Speaker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || SpeakerBox.SelectedValue is not string id) return;
        var device = _session.Outputs.FirstOrDefault(d => d.Id == id);
        if (device != null)
            _session.ChooseOutput(device);
    }

    private void FillDeviceBox(ComboBox box, IReadOnlyList<AudioDeviceInfo> devices, string? selectedId)
    {
        var same = box.Items.Count == devices.Count;
        if (same)
        {
            for (var i = 0; i < devices.Count; i++)
            {
                if (box.Items[i] is not AudioDeviceInfo item || item.Id != devices[i].Id)
                {
                    same = false;
                    break;
                }
            }
        }

        if (!same)
            box.ItemsSource = devices;
        if ((string?)box.SelectedValue != selectedId)
            box.SelectedValue = selectedId;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_exporting) return;
        if (e.OriginalSource is TextBox || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (e.Key == Key.Space && e.OriginalSource is not ComboBox && e.OriginalSource is not ComboBoxItem && e.OriginalSource is not Button)
        {
            _session.PlayPause();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            _session.Undo();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            _session.Redo();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.R)
        {
            Record_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            _session.Save();
            e.Handled = true;
        }
    }

    private async void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exporting)
        {
            e.Cancel = true;
            _session.Tell("Wait for the export to finish before closing RMS.");
            return;
        }
        if (_closeReady) return;
        e.Cancel = true;
        if (_closePending) return;
        _closePending = true;
        IsEnabled = false;
        try
        {
            await _session.ShutdownAsync();
            _closeReady = true;
            Close();
        }
        catch (Exception ex)
        {
            if (_session.Engine.HasTakeFinalizationError)
            {
                var exit = MessageBox.Show(
                    "RMS could not finish saving what you just recorded: " + ex.Message +
                    "\n\nThe song folder still has the audio that was written. Close anyway?",
                    "RMS", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (exit == MessageBoxResult.Yes)
                {
                    try
                    {
                        _session.ExitPreservingRecovery();
                        _closeReady = true;
                        Close();
                        return;
                    }
                    catch (Exception exitError)
                    {
                        MessageBox.Show("RMS could not close: " + exitError.Message, "RMS",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                _closePending = false;
                IsEnabled = true;
                return;
            }
            _closePending = false;
            IsEnabled = true;
            MessageBox.Show("RMS could not finish saving the song: " + ex.Message +
                "\n\nThe window is still open so the song is not lost.",
                "RMS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
