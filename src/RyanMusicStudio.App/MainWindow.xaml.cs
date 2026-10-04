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
    private static readonly SolidColorBrush PadOff = Freeze(new SolidColorBrush(Color.FromRgb(247, 251, 252)));
    private static readonly SolidColorBrush PadOn = Freeze(new SolidColorBrush(Color.FromRgb(14, 124, 102)));
    private static readonly SolidColorBrush Ink = Freeze(new SolidColorBrush(Color.FromRgb(26, 36, 40)));
    private static readonly SolidColorBrush Paper = Freeze(new SolidColorBrush(Color.FromRgb(247, 251, 252)));
    private static readonly SolidColorBrush Line = Freeze(new SolidColorBrush(Color.FromRgb(197, 208, 214)));
    private static readonly SolidColorBrush LampOn = Freeze(new SolidColorBrush(Color.FromRgb(242, 183, 5)));
    private static readonly SolidColorBrush LampOff = Freeze(new SolidColorBrush(Color.FromRgb(216, 224, 228)));
    private static readonly SolidColorBrush Recording = Freeze(new SolidColorBrush(Color.FromRgb(176, 48, 48)));

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

    public MainWindow()
    {
        _syncing = true;
        InitializeComponent();
        BuildPads();
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
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(92, 107, 114))
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
            FontSize = 18
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
        var busy = _session.Loading;
        StatusText.Text = _session.Status;
        SongName.Text = project == null ? "No song yet" : project.Name;
        Title = project == null ? "RMS" : project.Name + (project.Dirty ? " •" : "") + " — RMS";
        TempoText.Text = project == null ? "100" : ((int)Math.Round(project.TempoBpm)).ToString();
        var step = _session.CurrentStep + 1;
        var clock = project == null
            ? "00:00.000"
            : TimelineMath.FormatClock(_session.Engine.PlayheadFrames, project.SampleRate);
        PositionText.Text = "Step " + step + " of 16    " + clock;

        if (project != null && (int)VolumeSlider.Value != project.Studio.VolumePercent)
            VolumeSlider.Value = project.Studio.VolumePercent;
        VolumeText.Text = ((int)VolumeSlider.Value).ToString();

        var showBanner = busy || _session.Problem != null;
        Banner.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
        BannerText.Text = busy
            ? "Opening your song…"
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
        RecordButton.IsEnabled = enabled;
        RecordButton.Content = _session.Engine.IsRecording || _session.Engine.IsCountingIn
            ? "Stop recording"
            : "Record my voice";
        RecordButton.Background = _session.Engine.IsRecording ? Recording : Paper;
        RecordButton.Foreground = _session.Engine.IsRecording ? Paper : Ink;

        var playing = _session.Engine.IsSongPlaying;
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
                button.Foreground = on ? Paper : Ink;
                button.BorderBrush = s == _session.CurrentStep && playing ? LampOn : Line;
                button.BorderThickness = s == _session.CurrentStep && playing ? new Thickness(3) : new Thickness(1);
            }
        }

        for (var i = 0; i < _keys.Length; i++)
        {
            var selected = i == _session.MelodyPen;
            _keys[i].IsEnabled = enabled;
            _keys[i].Background = selected ? PadOn : Paper;
            _keys[i].Foreground = selected ? Paper : Ink;
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
            button.Foreground = on ? Paper : Ink;
            button.BorderBrush = s == _session.CurrentStep && playing ? LampOn : Line;
            button.BorderThickness = s == _session.CurrentStep && playing ? new Thickness(3) : new Thickness(1);
        }

        _syncing = false;
    }

    private void Play_Click(object sender, RoutedEventArgs e) => _session.PlayFromStart();
    private void Stop_Click(object sender, RoutedEventArgs e) => _session.StopSong();
    private void TempoDown_Click(object sender, RoutedEventArgs e) => _session.NudgeTempo(-4);
    private void TempoUp_Click(object sender, RoutedEventArgs e) => _session.NudgeTempo(4);
    private void Record_Click(object sender, RoutedEventArgs e) => _session.RecordVoice();

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
        if (e.Key == Key.Space && e.OriginalSource is not ComboBox && e.OriginalSource is not ComboBoxItem)
        {
            if (_session.Engine.IsSongPlaying || _session.Engine.IsTakeActive)
                _session.StopSong();
            else
                _session.PlayFromStart();
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
