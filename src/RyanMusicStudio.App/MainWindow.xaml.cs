using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Timeline;
using RyanMusicStudio.Engine.Devices;

namespace RyanMusicStudio.App;

public partial class MainWindow : Window
{
    private readonly SessionController _session = new();
    private bool _syncing;
    private int _refreshQueued;
    private bool _focusFromKeyboard;
    private ProjectDocument? _mixerProject;
    private string _mixerTracks = "";

    public MainWindow()
    {
        // BAML wires ValueChanged before it applies Slider.Minimum, so handlers fire mid-load.
        _syncing = true;
        InitializeComponent();
        _session.Changed += QueueRefresh;
        _session.Banner += msg => Dispatcher.InvokeAsync(() =>
            MessageBox.Show(msg, "RMS", MessageBoxButton.OK, MessageBoxImage.Information));
        Timeline.Seek = frame => _session.Seek(frame);
        Timeline.Zoomed = pixelsPerSecond => _session.Zoom = pixelsPerSecond;
        PreviewGotKeyboardFocus += (_, _) =>
            _focusFromKeyboard = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        Timeline.SelectClip = id => _session.SelectedClipId = id;
        Timeline.MoveClip = (id, start) =>
        {
            _session.SelectedClipId = id;
            _session.MoveSelected(start);
        };
        Timeline.ChoosePart = (takeId, start, end) => _session.ChoosePart(takeId, start, end);
        Timeline.SelectRange = (start, end) => _session.SetSelection(start, end);
        RefreshUi();
    }

    // Changed is also raised from audio and pool threads. Never block the caller: a synchronous
    // Invoke deadlocks against a UI thread that is waiting for the engine to finish a take.
    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        Dispatcher.InvokeAsync(() =>
        {
            Volatile.Write(ref _refreshQueued, 0);
            RefreshUi();
        }, DispatcherPriority.Background);
    }

    private void RefreshUi()
    {
        _syncing = true;
        StatusText.Text = _session.Status;
        LyricsText.Text = _session.Lyrics;
        ClockText.Text = _session.Project == null
            ? "00:00.000"
            : TimelineMath.FormatClock(_session.Engine.PlayheadFrames, _session.Project.SampleRate);
        InMeter.Level = _session.InputPeak;
        OutMeter.Level = _session.OutputPeak;
        InMeter.IsClipping = _session.Clipping;
        // Re-render even when the level is unchanged, so the peak-hold line can fall back.
        InMeter.InvalidateVisual();
        OutMeter.InvalidateVisual();
        ClipLabel.Visibility = _session.Clipping ? Visibility.Visible : Visibility.Collapsed;
        RecordBtn.Content = _session.Engine.IsRecording ? "Stop recording  (R)"
            : _session.Engine.IsCountingIn ? "Counting in…  (R cancels)"
            : "Record another take  (R)";
        Title = _session.Project == null
            ? "RMS — Ryan Music Studio"
            : $"{_session.Project.Name}{(_session.Project.Dirty ? " •" : "")} — RMS";

        HomePlace.Visibility = Vis(_session.Place == StudioPlace.Home);
        SetupPlace.Visibility = Vis(_session.Place == StudioPlace.Setup);
        ArrangePlace.Visibility = Vis(_session.Place == StudioPlace.Arrange);
        MixPlace.Visibility = Vis(_session.Place == StudioPlace.Mix);
        ExportPlace.Visibility = Vis(_session.Place == StudioPlace.Export);

        TabHome.IsChecked = _session.Place == StudioPlace.Home;
        TabSetup.IsChecked = _session.Place == StudioPlace.Setup;
        TabArrange.IsChecked = _session.Place == StudioPlace.Arrange;
        TabMix.IsChecked = _session.Place == StudioPlace.Mix;
        TabExport.IsChecked = _session.Place == StudioPlace.Export;

        if (_session.Place == StudioPlace.Home)
        {
            SetItems(RecentList, _session.Settings.Recent.Select(r => r.Name + "  —  " + r.Path).ToList());
        }

        if (_session.Place == StudioPlace.Setup)
        {
            InputBox.ItemsSource = _session.Inputs;
            OutputBox.ItemsSource = _session.Outputs;
            InputBox.SelectedItem = _session.SelectedInput;
            OutputBox.SelectedItem = _session.SelectedOutput;
            InputDetail.Text = _session.SelectedInput?.DetailLine
                ?? (!string.IsNullOrEmpty(_session.Settings.InputDeviceId)
                    ? "Your chosen microphone isn't plugged in. Plug it back in, or pick another from the list."
                    : "No microphone found. Plug in your USB mic or interface; it appears here on its own.");
            InputChannelRow.Visibility = Vis(_session.SelectedInput is { Channels: > 1 });
            InputChannelBox.SelectedIndex = Math.Clamp(_session.Settings.InputChannel, 0, 2);
            SetupMeter.Level = _session.InputPeak;
            SetupMeter.IsClipping = _session.Clipping;
            SetupMeter.InvalidateVisual();
            OutputDetail.Text = _session.SelectedOutput?.DetailLine
                ?? (!string.IsNullOrEmpty(_session.Settings.OutputDeviceId)
                    ? "Your chosen headphones aren't plugged in. Plug them back in, or pick another output from the list."
                    : "No headphone output found.");
            LatencyWarn.Text = _session.SelectedOutput != null && AudioDeviceService.LatencyUnsuitable(_session.SelectedOutput)
                ? "This output looks wireless or slow. Use wired headphones if the beat feels late while you sing."
                : "";
            ExclusiveBox.IsChecked = _session.Settings.ExclusiveMode;
            MonitorBox.IsChecked = _session.Settings.SoftwareMonitor;
            BufferSlider.Value = _session.Settings.BufferMilliseconds;
            BufferLabel.Text = $"{_session.Settings.BufferMilliseconds} ms";
            var ms = _session.Settings.UserRecordingOffsetFrames / (double)(_session.Project?.SampleRate ?? 48000) * 1000.0;
            OffsetSlider.Value = ms;
            OffsetLabel.Text = $"{ms:0} ms";
        }

        if (_session.Project != null)
        {
            SetItems(TrackList, _session.Project.Tracks.Select(DescribeTrack).ToList());
            var selected = _session.SelectedTrack();
            TrackList.SelectedIndex = selected == null ? -1 : _session.Project.Tracks.IndexOf(selected);
            SetItems(TakeList, selected?.Takes.Select(t => t.Name + TakeNote(selected, t)).ToList());
            LoopBox.IsChecked = _session.Project.Loop.Enabled;
            LoopRecBox.IsChecked = _session.Project.LoopRecording;
            PunchBox.IsChecked = _session.Project.Punch.Enabled;
            Timeline.Project = _session.Project;
            Timeline.Playhead = _session.Engine.PlayheadFrames;
            Timeline.PixelsPerSecond = _session.Zoom;
            Timeline.SelectedClipId = _session.SelectedClipId;
            Timeline.CompMode = _session.CompMode;
            Timeline.InvalidateProject();
            var songIsEmpty = _session.Project.Tracks.All(t => t.Clips.Count == 0 && t.Takes.Count == 0);
            ImportBtn.Style = (Style)FindResource(songIsEmpty ? "PrimaryButton" : "PlainButton");
            CompBtn.Style = (Style)FindResource(_session.CompMode ? "PrimaryButton" : "PlainButton");
            CompBtn.Content = _session.CompMode ? "Done choosing parts" : "Choose best parts";
            TakesHint.Text = _session.CompMode
                ? "Drag across the best part of a take on the timeline. Later choices replace earlier ones."
                : "Record several passes. Double-click a take to hear it, then choose the best parts.";
            if (_session.Place == StudioPlace.Mix)
                RebuildMixerIfChanged();
        }
        if (_session.Place != StudioPlace.Mix)
            _mixerProject = null;

        _syncing = false;
    }

    // Replacing ItemsSource clears the selection, so only do it when the text changed.
    private static void SetItems(ItemsControl list, List<string>? items)
    {
        if (list.ItemsSource is List<string> current && items != null && current.SequenceEqual(items)) return;
        if (list.ItemsSource == null && items == null) return;
        list.ItemsSource = items;
    }

    // RefreshUi runs on every 50 ms tick; rebuilding the strips each time destroys a fader mid-drag.
    private void RebuildMixerIfChanged()
    {
        var tracks = string.Join("|", _session.Project!.Tracks.Select(t => t.Id));
        if (ReferenceEquals(_mixerProject, _session.Project) && tracks == _mixerTracks) return;
        _mixerProject = _session.Project;
        _mixerTracks = tracks;
        RebuildMixer();
    }

    // Once parts are chosen, those are what plays; otherwise the auditioned take is.
    private static string TakeNote(Track track, Take take) =>
        track.Comp.Regions.Count > 0
            ? track.Comp.Regions.Any(r => r.TakeId == take.Id) ? "  · chosen parts" : ""
            : track.AuditionTakeId == take.Id ? "  · listening" : "";

    private static string DescribeTrack(Track t) =>
        $"{(t.Armed ? "● " : "")}{t.Name}" +
        $"{(t.Takes.Count == 1 ? "  · 1 take" : t.Takes.Count > 1 ? $"  · {t.Takes.Count} takes" : "")}" +
        $"{(t.Mute ? "  · mute" : "")}{(t.Solo ? "  · solo" : "")}";

    private void RebuildMixer()
    {
        MixerHost.Children.Clear();
        if (_session.Project == null) return;
        foreach (var track in _session.Project.Tracks)
        {
            var box = new Border
            {
                Width = 150,
                Margin = new Thickness(8),
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(42, 32, 24))
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = track.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var mute = new CheckBox { Content = "Mute", IsChecked = track.Mute, Margin = new Thickness(0, 8, 0, 0) };
            var solo = new CheckBox { Content = "Solo", IsChecked = track.Solo };
            var captured = track;
            mute.Click += (_, _) => _session.ToggleMute(captured);
            solo.Click += (_, _) => _session.ToggleSolo(captured);
            stack.Children.Add(mute);
            stack.Children.Add(solo);
            stack.Children.Add(new TextBlock { Text = "Volume", Margin = new Thickness(0, 10, 0, 0) });
            var gain = new Slider { Minimum = -36, Maximum = 12, Value = track.GainDb, Height = 140, Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            gain.ValueChanged += (_, e) => { if (!_syncing) _session.SetTrackGain(captured, e.NewValue); };
            stack.Children.Add(gain);
            stack.Children.Add(new TextBlock { Text = "Left / Right" });
            var pan = new Slider { Minimum = -1, Maximum = 1, Value = track.Pan };
            pan.ValueChanged += (_, e) => { if (!_syncing) _session.SetTrackPan(captured, e.NewValue); };
            stack.Children.Add(pan);
            if (track.Role == TrackRole.Vocal)
            {
                stack.Children.Add(new TextBlock { Text = "Vocal starting point", Margin = new Thickness(0, 10, 0, 4) });
                foreach (var preset in new[] { "Clean", "Warm", "Spacious" })
                {
                    var p = preset;
                    var btn = new Button { Content = p, Style = (Style)FindResource("PlainButton"), Margin = new Thickness(0, 0, 0, 6) };
                    btn.Click += (_, _) => _session.ApplyPreset(captured, p);
                    stack.Children.Add(btn);
                }
            }
            box.Child = stack;
            MixerHost.Children.Add(box);
        }

        var master = new Border
        {
            Width = 150,
            Margin = new Thickness(8),
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromRgb(54, 40, 30))
        };
        var ms = new StackPanel();
        ms.Children.Add(new TextBlock { Text = "Master", FontWeight = FontWeights.SemiBold });
        ms.Children.Add(new TextBlock { Text = "Limiter stays on so the export does not crackle.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 8, 0, 0) });
        var mg = new Slider { Minimum = -12, Maximum = 6, Value = _session.Project.Master.GainDb, Height = 140, Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        mg.ValueChanged += (_, e) =>
        {
            if (_syncing || _session.Project == null) return;
            _session.Project.Master.GainDb = e.NewValue;
            _session.Project.Touch();
            _session.Engine.NotifyProjectChanged();
        };
        ms.Children.Add(mg);
        master.Child = ms;
        MixerHost.Children.Add(master);
    }

    private static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private void Place_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton rb) return;
        _session.Go(Enum.Parse<StudioPlace>((string)rb.Tag));
    }

    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.CanSwitchSong()) return;
        var dlg = new NewProjectWindow(_session.Settings.LastProjectParent);
        if (dlg.ShowDialog() == true)
            _session.NewVocalSession(dlg.ProjectName, dlg.Folder, dlg.Tempo, dlg.Numerator, dlg.Denominator, dlg.SampleRate);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.CanSwitchSong()) return;
        var path = _session.BrowseOpenProject();
        if (path != null) _session.TryOpen(path);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => _session.Save();

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var path = _session.BrowseOpenProject("Choose an empty folder for the copy");
        if (path != null) _session.SaveAs(path);
    }

    private void Play_Click(object sender, RoutedEventArgs e) => _session.PlayPause();
    private void Stop_Click(object sender, RoutedEventArgs e) => _session.Stop();
    private void Record_Click(object sender, RoutedEventArgs e) => _session.Record();
    private void Home_Click(object sender, RoutedEventArgs e) => _session.GoToStart();
    private void Undo_Click(object sender, RoutedEventArgs e) => _session.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _session.Redo();
    private void Split_Click(object sender, RoutedEventArgs e) => _session.SplitSelected();
    private void Delete_Click(object sender, RoutedEventArgs e) => _session.DeleteSelected();
    private void Loop_Click(object sender, RoutedEventArgs e) => _session.ToggleLoop();
    private void Metronome_Click(object sender, RoutedEventArgs e) => _session.ToggleMetronome();
    private void GoHome_Click(object sender, RoutedEventArgs e) => _session.Go(StudioPlace.Home);
    private void GoSetup_Click(object sender, RoutedEventArgs e) => _session.Go(StudioPlace.Setup);
    private void GoArrange_Click(object sender, RoutedEventArgs e) => _session.Go(StudioPlace.Arrange);
    private void GoMix_Click(object sender, RoutedEventArgs e) => _session.Go(StudioPlace.Mix);
    private void GoExport_Click(object sender, RoutedEventArgs e) => _session.Go(StudioPlace.Export);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _session.Zoom = Math.Min(400, _session.Zoom * 1.2); RefreshUi(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _session.Zoom = Math.Max(20, _session.Zoom / 1.2); RefreshUi(); }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "RMS — Ryan Music Studio\nVersion 1.0\n\nA local, offline vocal recorder for Windows. No account. Not affiliated with any other DAW, guitar, or audio brand.\n\nKeyboard: Space play/stop · R record · Ctrl+S save · Ctrl+Z undo · Ctrl+Y redo · S split · Delete · Home · L loop · Ctrl++ / Ctrl+- zoom",
            "About RMS",
            MessageBoxButton.OK);
    }

    private void Recent_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedIndex < 0) return;
        var item = _session.Settings.Recent[RecentList.SelectedIndex];
        _session.TryOpen(item.Path);
    }

    private void Input_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || InputBox.SelectedItem is not AudioDeviceInfo d) return;
        _session.ChooseInput(d);
    }

    private void Output_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || OutputBox.SelectedItem is not AudioDeviceInfo d) return;
        _session.ChooseOutput(d);
    }

    private void Exclusive_Click(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _session.SetExclusive(ExclusiveBox.IsChecked == true);
    }

    private void Monitor_Click(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _session.SetMonitor(MonitorBox.IsChecked == true);
    }

    private void Buffer_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncing) _session.SetBuffer((int)BufferSlider.Value);
    }

    private void Offset_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncing) _session.SetUserOffsetMs(OffsetSlider.Value);
    }

    private void Tone_Click(object sender, RoutedEventArgs e) => _session.PlayTestTone();
    private async void TestTake_Click(object sender, RoutedEventArgs e) => await _session.RecordTestTakeAsync();
    private void SetupDone_Click(object sender, RoutedEventArgs e)
    {
        _session.ConfirmSetup();
        _session.Go(_session.Project == null ? StudioPlace.Home : StudioPlace.Arrange);
    }

    private void AddTrack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void AddGuitarTrack_Click(object sender, RoutedEventArgs e) => _session.AddRecordingTrack("Guitar", TrackRole.Audio);
    private void AddVocalTrack_Click(object sender, RoutedEventArgs e) => _session.AddRecordingTrack("Vocal", TrackRole.Vocal);
    private void ArmTrack_Click(object sender, RoutedEventArgs e) => _session.ArmSelectedTrack();
    private void ImportBacking_Click(object sender, RoutedEventArgs e) => _session.BrowseAndImport();
    private void ClearParts_Click(object sender, RoutedEventArgs e) => _session.ClearChosenParts();
    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => _session.RescanDevices(fromButton: true);

    private void InputChannel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && InputChannelBox.SelectedIndex >= 0)
            _session.SetInputChannel(InputChannelBox.SelectedIndex);
    }

    private void TrackList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _session.Project == null || TrackList.SelectedIndex < 0) return;
        _session.SelectedTrackId = _session.Project.Tracks[TrackList.SelectedIndex].Id;
        RefreshUi();
    }

    private void Take_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        var track = _session.SelectedTrack();
        if (track == null || TakeList.SelectedIndex < 0) return;
        var take = track.Takes[TakeList.SelectedIndex];
        _session.SelectedClipId = take.Id; // so Split/Fade/Nudge act on this take, not an older selection
        _session.AuditionTake(track, take);
    }

    private void CompMode_Click(object sender, RoutedEventArgs e)
    {
        _session.CompMode = !_session.CompMode;
        _session.Tell(_session.CompMode
            ? "Choose best parts: on the timeline, drag across the best part of a take. Do it for each part of the song."
            : "Done choosing. Press Space to hear your chosen parts together.");
    }

    private void Fade_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Project == null) return;
        var fade = _session.Project.SampleRate / 20;
        _session.FadeSelected(fade, fade);
    }

    private void StretchLate_Click(object sender, RoutedEventArgs e) => _session.NudgeSelected(+1);
    private void StretchEarly_Click(object sender, RoutedEventArgs e) => _session.NudgeSelected(-1);

    private void LoopRec_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Project == null || _syncing) return;
        _session.Project.LoopRecording = LoopRecBox.IsChecked == true;
        _session.Project.Touch();
    }

    private void Punch_Click(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _session.SetPunch(PunchBox.IsChecked == true);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var format = FormatBox.SelectedIndex switch
        {
            1 => ExportFormat.Wav24,
            2 => ExportFormat.Mp3,
            _ => ExportFormat.Wav16
        };
        var scope = ScopeBox.SelectedIndex switch
        {
            1 => ExportScope.SelectedRange,
            2 => ExportScope.VocalStem,
            3 => ExportScope.BackingStem,
            _ => ExportScope.WholeProject
        };
        if (_session.Project == null)
        {
            _session.Tell("Start a New Vocal Session or open a song first.");
            return;
        }
        if (scope == ExportScope.SelectedRange && !_session.HasSelection)
        {
            _session.Tell("No range is marked yet. On the Record page, drag on the ruler above the tracks, then export again.");
            return;
        }
        var dest = _session.BrowseExport(format);
        if (dest == null) return;
        try { await _session.ExportAsync(dest, format, scope); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RMS could not export", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"Your song is saved as {Path.GetFileName(dest)}.\n\nOpen its folder now?", "Export finished",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            Process.Start("explorer.exe", $"/select,\"{dest}\"");
    }

    // Handled on PreviewKeyDown so a control that was just clicked can't swallow Space/Home.
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // A control reached with Tab keeps its own Space/Home (toggle a checkbox, press a button, first row).
        if (_focusFromKeyboard && (e.Key is Key.Space or Key.Home) && Keyboard.FocusedElement is Control c && c != this)
            return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Space) { _session.PlayPause(); e.Handled = true; }
        else if (e.Key == Key.R && !ctrl) { _session.Record(); e.Handled = true; }
        else if (e.Key == Key.S && !ctrl) { _session.SplitSelected(); e.Handled = true; }
        else if (e.Key == Key.L && !ctrl) { _session.ToggleLoop(); e.Handled = true; }
        else if (e.Key == Key.Home) { _session.GoToStart(); e.Handled = true; }
        else if (e.Key == Key.Delete) { _session.DeleteSelected(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { _session.Save(); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { NewSession_Click(sender, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.O) { Open_Click(sender, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.I) { _session.BrowseAndImport(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z) { _session.Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { _session.Redo(); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { ZoomIn_Click(sender, e); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { ZoomOut_Click(sender, e); e.Handled = true; }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        foreach (var file in files.Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)))
        {
            if (_session.Project == null)
            {
                MessageBox.Show("Start a New Vocal Session first, then drop the backing track.", "RMS", MessageBoxButton.OK);
                return;
            }
            _session.ImportAudio(file);
        }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _session.Save();
        _session.Dispose();
    }
}
