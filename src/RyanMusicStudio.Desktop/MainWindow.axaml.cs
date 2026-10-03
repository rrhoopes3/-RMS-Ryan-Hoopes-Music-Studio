using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RyanMusicStudio.App;
using RyanMusicStudio.Core.Model;

namespace RyanMusicStudio.Desktop;

public partial class MainWindow : Window
{
    private readonly SessionController _session;
    private readonly TabControl _tabs = new();
    private readonly TextBlock _title = Text("No song open", 23, true);
    private readonly TextBlock _status = Text("Welcome to RMS");
    private readonly TextBlock _clock = Text("0:00 / 0:00");
    private readonly TextBlock _transport = Text("Ready");
    private readonly TextBox _name = new() { Text = "New song", PlaceholderText = "Song name" };
    private readonly TextBox _tempo = new() { Text = "90", Width = 85 };
    private readonly ListBox _recent = new() { Height = 180 };
    private readonly ComboBox _inputs = new() { MinWidth = 320 };
    private readonly ComboBox _outputs = new() { MinWidth = 320 };
    private readonly ComboBox _channels = new() { ItemsSource = new[] { "All", "Input 1", "Input 2" } };
    private readonly CheckBox _monitor = new() { Content = "Hear microphone in headphones" };
    private readonly Slider _buffer = new() { Minimum = 8, Maximum = 80, Width = 200 };
    private readonly TextBlock _bufferLabel = Text("20 ms");
    private readonly ProgressBar _inputPeak = new() { Minimum = 0, Maximum = 1, Height = 12 };
    private readonly ProgressBar _outputPeak = new() { Minimum = 0, Maximum = 1, Height = 12 };
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, Width = 520 };
    private readonly ListBox _tracks = new() { Height = 175 };
    private readonly ListBox _clips = new() { Height = 120 };
    private readonly ListBox _takes = new() { Height = 175 };
    private readonly TextBox _from = new() { Text = "0", PlaceholderText = "Start (seconds)", Width = 120 };
    private readonly TextBox _to = new() { Text = "10", PlaceholderText = "End (seconds)", Width = 120 };
    private readonly CheckBox _loop = new() { Content = "Loop" };
    private readonly CheckBox _punch = new() { Content = "Punch" };
    private readonly CheckBox _loopRecord = new() { Content = "Record each loop" };
    private readonly CheckBox _click = new() { Content = "Metronome" };
    private readonly Slider _gain = new() { Minimum = -30, Maximum = 12, Width = 260 };
    private readonly Slider _pan = new() { Minimum = -1, Maximum = 1, Width = 260 };
    private readonly TextBlock _mixTrack = Text("Select a track on Record", 18, true);
    private readonly CheckBox _mute = new() { Content = "Mute" };
    private readonly CheckBox _solo = new() { Content = "Solo" };
    private readonly ComboBox _format = new() { ItemsSource = new[] { "WAV 16-bit", "WAV 24-bit", "MP3" }, SelectedIndex = 0 };
    private readonly ComboBox _scope = new() { ItemsSource = new[] { "Whole song", "Marked range", "Vocals only", "Backing only" }, SelectedIndex = 0 };
    private Button _play = null!;
    private Button _record = null!;
    private Button _export = null!;
    private Button _cancelExport = null!;
    private readonly ProgressBar _exportProgress = new() { Minimum = 0, Maximum = 100, Height = 8 };
    private readonly TextBlock _exportLabel = Text("");
    private int _refreshQueued;
    private bool _sync;
    private bool _closeReady;
    private bool _closePending;
    private ProjectDocument? _listedProject;
    private string _trackSignature = "";
    private string _takeSignature = "";
    private string _clipSignature = "";
    private string _deviceSignature = "";
    private string _recentSignature = "";

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
    private static TextBlock Text(string text, double size = 14, bool bold = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = Brush(bold ? "#f4e9d9" : "#cebdab"), TextWrapping = TextWrapping.Wrap
    };
    private static StackPanel Stack(Orientation orientation, params Control[] children)
    {
        var stack = new StackPanel { Orientation = orientation, Spacing = 10 };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }
    private static StackPanel V(params Control[] children) => Stack(Orientation.Vertical, children);
    private static StackPanel H(params Control[] children) => Stack(Orientation.Horizontal, children);
    private static Border Card(Control child) => new()
    {
        Child = child, Padding = new Thickness(18), CornerRadius = new CornerRadius(12),
        Background = Brush("#251c18"), BorderBrush = Brush("#4c392e"), BorderThickness = new Thickness(1)
    };
    private static ScrollViewer Page(Control child) => new()
    {
        Content = child, Padding = new Thickness(24),
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
    };
    private Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(14, 8) };
        button.Click += (_, _) => Run(action);
        return button;
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { _session.Tell(ex.Message); }
    }

    public MainWindow()
    {
        InitializeComponent();
        Background = Brush("#130e0d");
        _session = new SessionController();
        _session.Changed += () =>
        {
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
            Dispatcher.UIThread.Post(() => { Volatile.Write(ref _refreshQueued, 0); Refresh(); });
        };
        _session.Banner += message => Dispatcher.UIThread.Post(() => _status.Text = message);
        Content = Build();
        Wire();
        Refresh();
        Closing += OnClosing;
        KeyDown += OnKeyDown;
    }

    private Control Build()
    {
        var header = new DockPanel { Margin = new Thickness(24, 18, 24, 12) };
        var actions = H(Button("New", () => _ = CreateAsync()), Button("Open", () => _ = OpenAsync()),
            Button("Save", _session.Save), Button("Save As", () => _ = SaveAsAsync()));
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(V(Text("RMS  /  RYAN MUSIC STUDIO", 12, true), _title));

        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = "Home", Content = Home() },
            new TabItem { Header = "Audio Setup", Content = Setup() },
            new TabItem { Header = "Record", Content = Record() },
            new TabItem { Header = "Mix", Content = Mix() },
            new TabItem { Header = "Export", Content = Export() }
        };
        _tabs.SelectionChanged += (_, _) =>
        {
            if (!_sync && _tabs.SelectedIndex >= 0) _session.Go((StudioPlace)_tabs.SelectedIndex);
        };
        _tabs.SelectedIndex = 0;
        _play = Button("Play", _session.PlayPause);
        _record = Button("Record another take", _session.Record);
        var footer = Card(V(_status, H(_play, _record, Button("Stop", _session.Stop),
            Button("Go to start", _session.GoToStart), _transport, _clock)));
        footer.Margin = new Thickness(24, 8, 24, 20);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(header, 0); Grid.SetRow(_tabs, 1); Grid.SetRow(footer, 2);
        grid.Children.Add(header); grid.Children.Add(_tabs); grid.Children.Add(footer);
        return grid;
    }

    private Control Home() => Page(V(Text("Make room for a new song.", 28, true),
        Text("Record vocals or guitar over a backing track. Your projects and every take stay on this computer."),
        Card(V(Text("New vocal session", 18, true), Text("Name"), _name,
            H(Text("Tempo (BPM)"), _tempo), Button("Choose folder and create", () => _ = CreateAsync()))),
        Card(V(Text("Recent songs", 18, true), _recent,
            Button("Open selected song", () =>
            {
                var i = _recent.SelectedIndex;
                if (i >= 0 && i < _session.Settings.Recent.Count)
                    _session.TryOpen(_session.Settings.Recent[i].Path);
            })))));

    private Control Setup() => Page(V(Text("Audio Setup", 28, true),
        Text("Wear wired headphones. Choose the microphone and output you want RMS to use."),
        Card(V(Text("Microphone", 17, true), _inputs, H(Text("Channel"), _channels), _inputPeak)),
        Card(V(Text("Headphones / speakers", 17, true), _outputs, _outputPeak)),
        Card(V(_monitor, H(Text("Buffer"), _buffer, _bufferLabel),
            H(Button("Refresh devices", () => _session.RescanDevices(true)),
                Button("Test tone", _session.PlayTestTone),
                Button("Test recording", () => _ = _session.RecordTestTakeAsync()),
                Button("Sounds good", () =>
                {
                    _session.ConfirmSetup();
                    _session.Go(_session.Project == null ? StudioPlace.Home : StudioPlace.Arrange);
                }))))));

    private Control Record()
    {
        return Page(V(Text("Record and arrange", 28, true),
            Card(H(Text("Song position"), _seek)),
            Card(V(Text("Tracks", 17, true), _tracks,
                H(Button("Import WAV / MP3", () => _ = ImportAsync()),
                    Button("Add vocal", () => _session.AddRecordingTrack("Vocal", TrackRole.Vocal)),
                    Button("Add guitar", () => _session.AddRecordingTrack("Guitar", TrackRole.Audio)),
                    Button("Arm selected", _session.ArmSelectedTrack)))),
            Card(V(Text("Clips on selected track", 17, true), _clips,
                H(Button("Split at playhead", _session.SplitSelected),
                    Button("Fade 50 ms", () =>
                    {
                        if (_session.Project != null)
                            _session.FadeSelected(_session.Project.SampleRate / 20, _session.Project.SampleRate / 20);
                    }),
                    Button("20 ms earlier", () => _session.NudgeSelected(-1)),
                    Button("20 ms later", () => _session.NudgeSelected(1))))),
            Card(V(Text("Takes on selected track", 17, true), _takes,
                H(Button("Hear take", Audition), Button("Choose marked part", ChoosePart),
                    Button("Clear chosen parts", _session.ClearChosenParts)))),
            Card(V(Text("Mark a range", 17, true),
                Text("Enter start and end in seconds. Use the range for comping, looping, punch-in or export."),
                H(_from, _to, Button("Mark range", MarkRange)),
                H(_loop, _punch, _loopRecord, _click),
                H(Button("Undo", _session.Undo), Button("Redo", _session.Redo),
                    Button("Split selected", _session.SplitSelected),
                    Button("Delete selected", _session.DeleteSelected))))));
    }

    private Control Mix() => Page(V(Text("Mix", 28, true),
        Text("Select a track on Record, then adjust its level and stereo position."),
        Card(V(_mixTrack, Text("Gain  /  -30 to +12 dB"), _gain,
            Text("Pan  /  left to right"), _pan, H(_mute, _solo),
            H(Button("Clean", () => Preset("Clean")), Button("Warm", () => Preset("Warm")),
                Button("Spacious", () => Preset("Spacious")))))));

    private Control Export()
    {
        _export = Button("Choose destination and export", () => _ = ExportAsync());
        _cancelExport = Button("Cancel export", _session.CancelExport);
        return Page(V(Text("Export your song", 28, true),
            Text("Export the same takes, chosen parts and effects you hear in RMS."),
            Card(V(Text("Format", 17, true), _format, Text("What to export", 17, true), _scope,
                H(_export, _cancelExport), _exportProgress, _exportLabel,
                Text("WAV and MP3 import, and MP3 export, require ffmpeg. WAV export works without it.")))));
    }

    private void Wire()
    {
        _inputs.SelectionChanged += (_, _) =>
        {
            if (!_sync && _inputs.SelectedIndex >= 0 && _inputs.SelectedIndex < _session.Inputs.Count)
                Run(() => _session.ChooseInput(_session.Inputs[_inputs.SelectedIndex]));
        };
        _outputs.SelectionChanged += (_, _) =>
        {
            if (!_sync && _outputs.SelectedIndex >= 0 && _outputs.SelectedIndex < _session.Outputs.Count)
                Run(() => _session.ChooseOutput(_session.Outputs[_outputs.SelectedIndex]));
        };
        _channels.SelectionChanged += (_, _) =>
        {
            if (!_sync && _channels.SelectedIndex >= 0) _session.SetInputChannel(_channels.SelectedIndex);
        };
        _monitor.Click += (_, _) => { if (!_sync) _session.SetMonitor(_monitor.IsChecked == true); };
        _buffer.PropertyChanged += (_, e) =>
        {
            if (!_sync && e.Property == Slider.ValueProperty) _session.SetBuffer((int)_buffer.Value);
        };
        _tracks.SelectionChanged += (_, _) =>
        {
            if (_sync || _session.Project == null || _tracks.SelectedIndex < 0) return;
            _session.SelectedTrackId = _session.Project.Tracks[_tracks.SelectedIndex].Id;
            _takeSignature = ""; _clipSignature = ""; Refresh();
        };
        _clips.SelectionChanged += (_, _) =>
        {
            if (_sync || _clips.SelectedIndex < 0) return;
            var track = _session.SelectedTrack();
            if (track != null && _clips.SelectedIndex < track.Clips.Count)
                _session.SelectedClipId = track.Clips[_clips.SelectedIndex].Id;
        };
        _takes.SelectionChanged += (_, _) =>
        {
            if (_sync || _takes.SelectedIndex < 0) return;
            var track = _session.SelectedTrack();
            if (track != null && _takes.SelectedIndex < track.Takes.Count)
                _session.SelectedClipId = track.Takes[_takes.SelectedIndex].Id;
        };
        _seek.PropertyChanged += (_, e) =>
        {
            if (!_sync && e.Property == Slider.ValueProperty && _session.Project != null)
                _session.Seek((long)(_seek.Value * _session.Project.SampleRate));
        };
        _loop.Click += (_, _) => { if (!_sync) _session.ToggleLoop(); };
        _punch.Click += (_, _) => { if (!_sync) _session.SetPunch(_punch.IsChecked == true); };
        _loopRecord.Click += (_, _) =>
        {
            if (_sync || _session.Project == null) return;
            _session.Project.LoopRecording = _loopRecord.IsChecked == true;
            _session.Project.Touch();
        };
        _click.Click += (_, _) => { if (!_sync) _session.ToggleMetronome(); };
        _gain.PropertyChanged += (_, e) =>
        {
            if (_sync || e.Property != Slider.ValueProperty) return;
            var track = _session.SelectedTrack(); if (track != null) _session.SetTrackGain(track, _gain.Value);
        };
        _pan.PropertyChanged += (_, e) =>
        {
            if (_sync || e.Property != Slider.ValueProperty) return;
            var track = _session.SelectedTrack(); if (track != null) _session.SetTrackPan(track, _pan.Value);
        };
        _mute.Click += (_, _) =>
        {
            if (_sync) return; var track = _session.SelectedTrack(); if (track != null) _session.ToggleMute(track);
        };
        _solo.Click += (_, _) =>
        {
            if (_sync) return; var track = _session.SelectedTrack(); if (track != null) _session.ToggleSolo(track);
        };
    }

    private void Refresh()
    {
        if (_sync) return;
        _sync = true;
        try
        {
            var p = _session.Project;
            _title.Text = p?.Name ?? "No song open";
            Title = p == null ? "RMS — Ryan Music Studio" : $"{p.Name}{(p.Dirty ? " •" : "")} — RMS";
            _status.Text = _session.Status;
            _transport.Text = _session.Engine.IsRecording ? "● Recording" :
                _session.Engine.IsCountingIn ? "Count-in" : _session.Engine.IsTakeActive ? "Saving take…" :
                _session.Engine.IsPlaying ? "Playing" : "Ready";
            var savingTake = _session.Engine.IsTakeActive && !_session.Engine.IsRecording && !_session.Engine.IsCountingIn;
            _play.Content = _session.Engine.IsTakeActive ? "Finish take" : _session.Engine.IsPlaying ? "Stop playback" : "Play";
            _record.Content = _session.Engine.IsCountingIn ? "Cancel count-in" : savingTake ? "Saving take…" :
                _session.Engine.IsRecording ? "Finish take" : "Record another take";
            _play.IsEnabled = _record.IsEnabled = p != null && !savingTake && !_session.Engine.IsTestTaking;
            _export.IsEnabled = p != null && !_session.IsExporting && !_session.Engine.IsTakeActive && !_session.Engine.IsTestTaking;
            _format.IsEnabled = _scope.IsEnabled = !_session.IsExporting;
            _cancelExport.IsVisible = _exportProgress.IsVisible = _exportLabel.IsVisible = _session.IsExporting;
            _exportProgress.Value = _session.ExportProgress * 100;
            _exportLabel.Text = $"Exporting · {_session.ExportProgress:P0}";
            _inputPeak.Value = Math.Clamp(_session.InputPeak, 0, 1);
            _outputPeak.Value = Math.Clamp(_session.OutputPeak, 0, 1);
            _buffer.Value = _session.Settings.BufferMilliseconds;
            _bufferLabel.Text = $"{_session.Settings.BufferMilliseconds} ms";
            _monitor.IsChecked = _session.Settings.SoftwareMonitor;
            _channels.SelectedIndex = _session.Settings.InputChannel;
            var deviceSignature = string.Join('|', _session.Inputs.Select(d => d.Id)) + " / " +
                                  string.Join('|', _session.Outputs.Select(d => d.Id));
            if (deviceSignature != _deviceSignature)
            {
                _deviceSignature = deviceSignature;
                _inputs.ItemsSource = _session.Inputs.Select(d => d.Name + "  ·  " + d.DetailLine).ToArray();
                _outputs.ItemsSource = _session.Outputs.Select(d => d.Name + "  ·  " + d.DetailLine).ToArray();
            }
            _inputs.SelectedIndex = _session.SelectedInput == null ? -1 : _session.Inputs.ToList().FindIndex(d => d.Id == _session.SelectedInput.Id);
            _outputs.SelectedIndex = _session.SelectedOutput == null ? -1 : _session.Outputs.ToList().FindIndex(d => d.Id == _session.SelectedOutput.Id);
            var recentSignature = string.Join('|', _session.Settings.Recent.Select(r => r.Path));
            if (recentSignature != _recentSignature)
            {
                _recentSignature = recentSignature;
                _recent.ItemsSource = _session.Settings.Recent.Select(r => r.Name + "  —  " + r.Path).ToArray();
            }
            if (_tabs.SelectedIndex != (int)_session.Place) _tabs.SelectedIndex = (int)_session.Place;

            var rate = p?.SampleRate ?? 48000;
            var frame = _session.Engine.PlayheadFrames;
            _clock.Text = $"{Clock(frame, rate)} / {Clock(p?.LengthFrames() ?? 0, rate)}";
            _seek.Maximum = Math.Max(1, (p?.LengthFrames() ?? rate) / (double)rate);
            _seek.Value = Math.Min(_seek.Maximum, frame / (double)rate);
            _loop.IsChecked = p?.Loop.Enabled == true;
            _punch.IsChecked = p?.Punch.Enabled == true;
            _loopRecord.IsChecked = p?.LoopRecording == true;
            _click.IsChecked = _session.Engine.MetronomeEnabled;

            var signature = p == null ? "" : string.Join('|', p.Tracks.Select(t =>
                $"{t.Id}:{t.Name}:{t.Takes.Count}:{t.Clips.Count}:{t.Armed}:{t.Mute}:{t.Solo}"));
            if (!ReferenceEquals(_listedProject, p) || signature != _trackSignature)
            {
                _listedProject = p; _trackSignature = signature;
                _tracks.ItemsSource = p?.Tracks.Select(t =>
                    $"{(t.Armed ? "● " : "")}{t.Name}  ·  {t.Takes.Count} takes  ·  {t.Clips.Count} clips").ToArray();
                _tracks.SelectedIndex = p == null ? -1 : p.Tracks.FindIndex(t => t.Id == _session.SelectedTrackId);
                _takeSignature = ""; _clipSignature = "";
            }
            var selected = _session.SelectedTrack();
            var clipSignature = selected == null ? "" : selected.Id + ":" +
                string.Join('|', selected.Clips.Select(c => c.Id + c.StartFrame + c.LengthFrames));
            if (clipSignature != _clipSignature)
            {
                _clipSignature = clipSignature;
                _clips.ItemsSource = selected?.Clips.Select(c =>
                    $"{p?.FindMedia(c.MediaId)?.OriginalFileName ?? "Audio"}  ·  {Clock(c.StartFrame, rate)} – {Clock(c.EndFrame, rate)}").ToArray();
                _clips.SelectedIndex = selected?.Clips.FindIndex(c => c.Id == _session.SelectedClipId) ?? -1;
            }
            var takeSignature = selected == null ? "" : selected.Id + ":" +
                string.Join('|', selected.Takes.Select(t => $"{t.Id}:{t.StartFrame}:{t.LengthFrames}"));
            if (takeSignature != _takeSignature)
            {
                _takeSignature = takeSignature;
                _takes.ItemsSource = selected?.Takes.Select(t =>
                    $"{t.Name}  ·  {Clock(t.StartFrame, rate)} – {Clock(t.StartFrame + t.LengthFrames, rate)}").ToArray();
                _takes.SelectedIndex = selected?.Takes.FindIndex(t => t.Id == _session.SelectedClipId) ?? -1;
            }
            _mixTrack.Text = selected?.Name ?? "Select a track on Record";
            _gain.Value = selected?.GainDb ?? 0;
            _pan.Value = selected?.Pan ?? 0;
            _mute.IsChecked = selected?.Mute == true;
            _solo.IsChecked = selected?.Solo == true;
        }
        finally { _sync = false; }
    }

    private static string Clock(long frame, int rate)
    {
        var seconds = Math.Max(0, frame / Math.Max(1, rate));
        return $"{seconds / 60}:{seconds % 60:00}";
    }
    private void Audition()
    {
        var track = _session.SelectedTrack();
        if (track != null && _takes.SelectedIndex >= 0 && _takes.SelectedIndex < track.Takes.Count)
            _session.AuditionTake(track, track.Takes[_takes.SelectedIndex]);
    }
    private bool Range(out long start, out long end)
    {
        start = end = 0;
        var p = _session.Project;
        if (p == null || !double.TryParse(_from.Text, out var a) ||
            !double.TryParse(_to.Text, out var b) || !double.IsFinite(a) || !double.IsFinite(b) ||
            a < 0 || b <= a || b * p.SampleRate >= long.MaxValue)
        { _session.Tell("Enter valid start and end seconds."); return false; }
        start = (long)(a * p.SampleRate); end = (long)(b * p.SampleRate);
        return true;
    }
    private void MarkRange()
    {
        if (Range(out var start, out var end)) _session.SetSelection(start, end);
    }
    private void ChoosePart()
    {
        var track = _session.SelectedTrack();
        if (track == null || _takes.SelectedIndex < 0 || _takes.SelectedIndex >= track.Takes.Count) return;
        if (Range(out var start, out var end)) _session.ChoosePart(track.Takes[_takes.SelectedIndex].Id, start, end);
    }
    private void Preset(string name)
    {
        var track = _session.SelectedTrack();
        if (track != null) _session.ApplyPreset(track, name);
    }

    private async Task CreateAsync()
    {
        if (!_session.CanSwitchSong()) return;
        if (string.IsNullOrWhiteSpace(_name.Text)) { _session.Tell("Give the song a name first."); return; }
        if (!double.TryParse(_tempo.Text, out var bpm) || !double.IsFinite(bpm) || bpm is < 20 or > 300)
        { _session.Tell("Tempo must be between 20 and 300 BPM."); return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose the parent folder for this song", AllowMultiple = false });
        var path = folders.FirstOrDefault()?.Path.LocalPath;
        if (path != null) Run(() => _session.NewVocalSession(_name.Text ?? "New song", path, bpm, 4, 4, 48000));
    }
    private async Task OpenAsync()
    {
        if (!_session.CanSwitchSong()) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Open an RMS project folder", AllowMultiple = false });
        var path = folders.FirstOrDefault()?.Path.LocalPath;
        if (path != null) Run(() => _session.TryOpen(path));
    }
    private async Task SaveAsAsync()
    {
        if (_session.Project == null) { _session.Tell("Create or open a song first."); return; }
        if (!_session.CanSwitchSong()) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose an empty folder for the copy", AllowMultiple = false });
        var path = folders.FirstOrDefault()?.Path.LocalPath;
        if (path != null) Run(() => _session.SaveAs(path));
    }
    private async Task ImportAsync()
    {
        if (_session.Project == null) { _session.Tell("Create or open a song first."); return; }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a WAV or MP3 backing track", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Audio") { Patterns = new[] { "*.wav", "*.mp3" } } }
        });
        var path = files.FirstOrDefault()?.Path.LocalPath;
        if (path != null) Run(() => _session.ImportAudio(path));
    }
    private async Task ExportAsync()
    {
        var p = _session.Project;
        if (p == null) { _session.Tell("Create or open a song first."); return; }
        var format = _format.SelectedIndex switch { 1 => ExportFormat.Wav24, 2 => ExportFormat.Mp3, _ => ExportFormat.Wav16 };
        var scope = _scope.SelectedIndex switch
        { 1 => ExportScope.SelectedRange, 2 => ExportScope.VocalStem, 3 => ExportScope.BackingStem, _ => ExportScope.WholeProject };
        if (scope == ExportScope.SelectedRange && !_session.HasSelection)
        { _session.Tell("Mark a range on Record first."); return; }
        var ext = format == ExportFormat.Mp3 ? "mp3" : "wav";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export song", SuggestedFileName = p.Name + "." + ext,
            FileTypeChoices = new[] { new FilePickerFileType(ext.ToUpperInvariant()) { Patterns = new[] { "*." + ext } } }
        });
        var path = file?.Path.LocalPath;
        if (path == null) return;
        try { await _session.ExportAsync(path, format, scope); }
        catch (Exception ex) { _session.Tell("Export failed: " + ex.Message); }
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        var textInput = e.Source is Visual source && source.GetSelfAndVisualAncestors().Any(v => v is TextBox);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        if (textInput && !(command && e.Key == Key.S)) return;
        if (command && e.Key == Key.S) Run(_session.Save);
        else if (command && e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Run(_session.Redo);
        else if (command && e.Key == Key.Z) Run(_session.Undo);
        else if (command && e.Key == Key.Y) Run(_session.Redo);
        else if (command) return;
        else if (e.Source is Slider or ComboBox or Avalonia.Controls.Button or CheckBox) return;
        else if (e.Key == Key.Space) Run(_session.PlayPause);
        else if (e.Key == Key.R) Run(_session.Record);
        else if (e.Key == Key.L) Run(_session.ToggleLoop);
        else if (e.Key == Key.Home) Run(_session.GoToStart);
        else return;
        e.Handled = true;
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
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
            _status.Text = "Could not finish saving: " + ex.Message + " The window remains open for recovery.";
            IsEnabled = true; _closePending = false;
        }
    }
}
