using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using RyanMusicStudio.Core.Dsp;
using RyanMusicStudio.Core.Model;

namespace RyanMusicStudio.App.Controls;

/// <summary>A mixer whose rows survive transport refreshes. Call all members on the UI thread.</summary>
public sealed class TrackMixerPanel : UserControl
{
    private readonly StackPanel _rows = new();
    private readonly Dictionary<string, TrackRow> _tracks = new();
    private readonly TextBlock _empty = new() { Text = "Add or record audio to start mixing.", Margin = new Thickness(12) };
    private ProjectDocument? _project;
    private string? _selectedTrackId;

    public Action<Track>? SelectTrack { get; set; }
    public Action<Track>? ToggleMute { get; set; }
    public Action<Track>? ToggleSolo { get; set; }
    public Action<Track, double>? SetTrackGain { get; set; }
    public Action<Track, double>? SetTrackPan { get; set; }
    public Action<Track, string>? ApplyPreset { get; set; }
    /// <summary>Optional linear peak reader, where 1 is full scale. Never called on the audio thread.</summary>
    public Func<Track, double>? ReadTrackLevel { get; set; }

    public string? SelectedTrackId
    {
        get => _selectedTrackId;
        set { if (_selectedTrackId == value) return; _selectedTrackId = value; Refresh(); }
    }

    public TrackMixerPanel()
    {
        Foreground = Brush("#F3E6D4");
        Content = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        AutomationProperties.SetName(this, "Track mixer");
        Refresh();
    }

    public void SetProject(ProjectDocument? project, string? selectedTrackId = null)
    {
        _project = project;
        _selectedTrackId = selectedTrackId;
        Refresh();
    }

    /// <summary>Updates existing rows in place, including after undo replaces track objects.</summary>
    public void Refresh()
    {
        var tracks = _project?.Tracks;
        var ids = tracks?.Select(t => t.Id).ToHashSet() ?? [];
        foreach (var id in _tracks.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _rows.Children.Remove(_tracks[id].Root);
            _tracks.Remove(id);
        }
        if (tracks == null || tracks.Count == 0)
        {
            if (!_rows.Children.Contains(_empty)) _rows.Children.Add(_empty);
            return;
        }
        _rows.Children.Remove(_empty);
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            if (!_tracks.TryGetValue(track.Id, out var row))
            {
                row = new TrackRow(this, track);
                _tracks.Add(track.Id, row);
            }
            if (_rows.Children.IndexOf(row.Root) != i)
            {
                _rows.Children.Remove(row.Root);
                _rows.Children.Insert(i, row.Root);
            }
            row.Refresh(track, track.Id == _selectedTrackId);
            if (ReadTrackLevel != null) row.SetLevel(ReadTrackLevel(track));
        }
    }

    public void SetTrackLevel(string trackId, double linearPeak)
    {
        if (_tracks.TryGetValue(trackId, out var row)) row.SetLevel(linearPeak);
    }

    public void ClearLevels()
    {
        foreach (var row in _tracks.Values) row.SetLevel(0);
    }

    private void Choose(Track track)
    {
        _selectedTrackId = track.Id;
        SelectTrack?.Invoke(track);
        Refresh();
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private sealed class TrackRow
    {
        private static readonly Brush Copper = Brush("#C4844A");
        private static readonly Brush Line = Brush("#5A4636");
        private static readonly Brush Panel = Brush("#221A15");
        private static readonly Brush Active = Brush("#4A3A2C");
        private static readonly Brush Cream = Brush("#F3E6D4");
        private static readonly Brush Red = Brush("#E87973");
        private static readonly Brush Green = Brush("#91B77A");
        private readonly Button _name;
        private readonly TextBlock _state = new() { FontSize = 11, Margin = new Thickness(4, 3, 4, 5) };
        private readonly Button _mute;
        private readonly Button _solo;
        private readonly Slider _gain = new() { Minimum = -60, Maximum = 12, SmallChange = 0.5, LargeChange = 3 };
        private readonly Slider _pan = new() { Minimum = -1, Maximum = 1, SmallChange = 0.05, LargeChange = 0.25 };
        private readonly TextBlock _gainValue = new() { Width = 68, TextAlignment = TextAlignment.Right };
        private readonly TextBlock _panValue = new() { Width = 68, TextAlignment = TextAlignment.Right };
        private readonly ComboBox _preset = new() { ItemsSource = VocalPresets.Names, MinWidth = 110, Margin = new Thickness(4, 5, 4, 2) };
        private readonly ProgressBar _level = new() { Minimum = -60, Maximum = 0, Value = -60, Height = 5, Margin = new Thickness(4, 6, 4, 1), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly List<(string Name, Track Track)> _presets = VocalPresets.Names.Select(name =>
        {
            var track = new Track();
            VocalPresets.Apply(track, name);
            return (name, track);
        }).ToList();
        private Track _track;
        private bool _refreshing;
        public Border Root { get; }

        public TrackRow(TrackMixerPanel owner, Track track)
        {
            _track = track;
            _name = MakeButton(track.Name);
            _name.HorizontalContentAlignment = HorizontalAlignment.Left;
            _name.Click += (_, _) => owner.Choose(_track);
            _mute = MakeButton("Mute");
            _solo = MakeButton("Solo");
            _mute.Click += (_, _) => { owner.ToggleMute?.Invoke(_track); owner.Refresh(); };
            _solo.Click += (_, _) => { owner.ToggleSolo?.Invoke(_track); owner.Refresh(); };
            var top = new DockPanel();
            DockPanel.SetDock(_solo, Dock.Right);
            DockPanel.SetDock(_mute, Dock.Right);
            top.Children.Add(_solo);
            top.Children.Add(_mute);
            top.Children.Add(_name);
            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(_state);
            stack.Children.Add(SliderLine("Gain", _gain, _gainValue));
            stack.Children.Add(SliderLine("Pan", _pan, _panValue));
            _preset.ToolTip = "Apply a vocal starting point: Clean, Warm or Spacious. This replaces the current effect chain.";
            stack.Children.Add(_preset);
            stack.Children.Add(_level);
            Root = new Border { Child = stack, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 7), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), Background = Panel };
            _gain.ValueChanged += (_, _) =>
            {
                _gainValue.Text = _gain.Value.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
                if (!_refreshing) owner.SetTrackGain?.Invoke(_track, _gain.Value);
            };
            _pan.ValueChanged += (_, _) =>
            {
                _panValue.Text = PanText(_pan.Value);
                if (!_refreshing) owner.SetTrackPan?.Invoke(_track, _pan.Value);
            };
            _preset.SelectionChanged += (_, _) =>
            {
                if (!_refreshing && _preset.SelectedItem is string name)
                {
                    owner.ApplyPreset?.Invoke(_track, name);
                    owner.Refresh();
                }
            };
        }

        public void Refresh(Track track, bool selected)
        {
            _track = track;
            _refreshing = true;
            try
            {
                Root.BorderBrush = selected ? Copper : Line;
                _name.Content = track.Name;
                _name.Background = selected ? Active : Panel;
                _state.Text = string.Join("  ·  ", new[] { track.Role.ToString(), selected ? "SELECTED" : null, track.Armed ? "● ARMED" : null }.Where(s => s != null));
                _state.Foreground = track.Armed ? Red : Cream;
                _mute.Content = track.Mute ? "Muted" : "Mute";
                _solo.Content = track.Solo ? "Solo on" : "Solo";
                _mute.Background = track.Mute ? Active : Panel;
                _solo.Background = track.Solo ? Active : Panel;
                if (!_gain.IsMouseCaptureWithin) _gain.Value = track.GainDb;
                if (!_pan.IsMouseCaptureWithin) _pan.Value = track.Pan;
                _gainValue.Text = _gain.Value.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
                _panValue.Text = PanText(_pan.Value);
                _preset.Visibility = track.Role == TrackRole.Vocal ? Visibility.Visible : Visibility.Collapsed;
                if (!_preset.IsDropDownOpen && !_preset.IsKeyboardFocusWithin)
                    _preset.SelectedItem = _presets.FirstOrDefault(p => SameEffects(p.Track, track)).Name;
                foreach (var (control, action) in new (DependencyObject, string)[] { (_name, "Select"), (_mute, track.Mute ? "Unmute" : "Mute"), (_solo, track.Solo ? "Disable solo" : "Solo"), (_gain, "Gain in decibels"), (_pan, "Pan, left to right"), (_preset, "Vocal preset"), (_level, "Peak level") })
                    AutomationProperties.SetName(control, action + " — " + track.Name);
            }
            finally { _refreshing = false; }
        }

        public void SetLevel(double peak)
        {
            _level.Visibility = Visibility.Visible;
            var db = double.IsFinite(peak) && peak > 0 ? Math.Clamp(20 * Math.Log10(peak), -60, 0) : -60;
            _level.Value = db;
            _level.Foreground = peak >= 1 ? Red : Green;
        }

        private static string PanText(double pan) => Math.Abs(pan) < 0.005 ? "Center" : $"{(pan < 0 ? "L" : "R")} {Math.Abs(pan) * 100:0}%";

        private static bool SameEffects(Track a, Track b) => a.Effects.Count == b.Effects.Count && a.Effects.Zip(b.Effects).All(pair =>
            pair.First.Kind == pair.Second.Kind && pair.First.Bypass == pair.Second.Bypass &&
            pair.First.Parameters.Count == pair.Second.Parameters.Count && pair.First.Parameters.All(p => pair.Second.Parameters.TryGetValue(p.Key, out var value) && value == p.Value));

        private static Button MakeButton(string content) => new()
        {
            Content = content, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(3, 0, 3, 0),
            Background = Panel, Foreground = Cream, BorderBrush = Line,
            Style = Application.Current?.TryFindResource("PlainButton") as Style,
            FontSize = 12, MinHeight = 28
        };

        private static Grid SliderLine(string label, Slider slider, TextBlock value)
        {
            var grid = new Grid { Margin = new Thickness(4, 3, 4, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(value, 2);
            value.FontSize = 12;
            value.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(slider);
            grid.Children.Add(value);
            return grid;
        }
    }
}
