using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Timeline;
using RyanMusicStudio.Engine.Media;
using RyanMusicStudio.Engine.Audio;

namespace RyanMusicStudio.App.Controls;

public sealed class TimelinePanel : FrameworkElement
{
    private const double Header = 28.0;
    private const double TrackLabel = 24.0;
    private const double MinLane = 38.0;
    // Lanes stop growing here so two takes don't each fill half the screen.
    private const double MaxLane = 120.0;
    // A track with nothing on it yet collapses to a short drop-zone strip.
    private const double EmptyTrack = 76.0;

    private static readonly Typeface Ui = new("Segoe UI");
    private static readonly Typeface UiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush Cream = Frozen(Color.FromRgb(232, 236, 241));
    private static readonly Brush Muted = Frozen(Color.FromRgb(154, 163, 174));
    private static readonly Brush Copper = Frozen(Color.FromRgb(31, 145, 80));
    private static readonly Brush RecordRed = Frozen(Color.FromRgb(229, 72, 77));

    private readonly WaveformCache _waves = new();
    private readonly Dictionary<string, PeakData> _peaks = new();
    private readonly HashSet<string> _noPeaks = [];
    private readonly HashSet<string> _building = [];
    private Point _dragStart;
    private long _dragClipStart;
    private bool _dragging;
    private long _playhead;
    private double _verticalOffset;
    private RecordingWaveformSnapshot? _liveRecording;

    // Ruler drag: marks a range for loop, punch-in and export.
    private bool _rangeDragging;
    private long _rangeStart;
    private long _rangeEnd;

    // "Choose best parts" drag across a take lane.
    private Take? _compTake;
    private long _compStart;
    private long _compEnd;

    public ProjectDocument? Project { get; set; }
    public double PixelsPerSecond { get; set; } = 80;
    public string? SelectedClipId { get; set; }
    public string? SelectedTrackId { get; set; }
    public Action<string>? SelectTrack { get; set; }
    public RecordingWaveformSnapshot? LiveRecording
    {
        get => _liveRecording;
        set { _liveRecording = value; InvalidateVisual(); }
    }
    public bool CompMode { get; set; }
    public Action<long>? Seek { get; set; }
    public Action<string, long>? MoveClip { get; set; }
    public Action<string>? SelectClip { get; set; }
    public Action<double>? Zoomed { get; set; }
    public Action<string, long, long>? ChoosePart { get; set; }
    public Action<long, long>? SelectRange { get; set; }

    /// <summary>First frame shown at the left edge; the view follows the playhead at its right margin.</summary>
    public long ViewStartFrame { get; private set; }

    public long Playhead
    {
        get => _playhead;
        set
        {
            if (value == _playhead) return;
            _playhead = value;
            FollowPlayhead();
            InvalidateVisual();
        }
    }

    public TimelinePanel()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        ToolTip = "Click a track to select it. Wheel scrolls tracks; Shift+wheel scrolls time; Ctrl+wheel zooms.";
    }

    public void InvalidateProject() => InvalidateVisual();

    /// <summary>
    /// One horizontal strip of a track: its imported clips, or one recorded take. Area is drawn;
    /// Slot is the full band (no gaps) used for clicks, so even very thin lanes hit the right take.
    /// </summary>
    private readonly record struct Lane(Track Track, Take? Take, Rect Area, Rect Slot, bool IsLive = false);

    private int LaneCount(Track track) =>
        (track.Clips.Count > 0 || track.Takes.Count == 0 ? 1 : 0) + track.Takes.Count +
        (LiveRecording?.TrackId == track.Id && track.Takes.Count > 0 ? 1 : 0);

    private bool HasContent(Track track) =>
        track.Clips.Count > 0 || track.Takes.Count > 0 || LiveRecording?.TrackId == track.Id;

    private double TrackHeight(Track track)
    {
        if (Project == null) return EmptyTrack;
        var tracks = Project.Tracks;
        var avail = Math.Max(0, ActualHeight - Header);
        var withContent = tracks.Count(HasContent);
        double Natural(Track t) => TrackLabel + 6 + LaneCount(t) * MinLane;
        if (withContent == 0)
            // Nothing recorded yet: split the board evenly so the drop hints have room.
            return Math.Max(Natural(track), Math.Min(220, avail / Math.Max(1, tracks.Count)));
        if (!HasContent(track))
            return EmptyTrack;
        var share = (avail - (tracks.Count - withContent) * EmptyTrack) / withContent;
        var cap = TrackLabel + 6 + LaneCount(track) * MaxLane;
        return Math.Max(Natural(track), Math.Min(cap, share));
    }

    private double ContentHeight => Project?.Tracks.Sum(TrackHeight) ?? 0;

    // Each take gets its own lane so a singer can see, hear and pick between passes.
    private List<Lane> LayoutLanes()
    {
        var lanes = new List<Lane>();
        if (Project == null) return lanes;
        var w = ActualWidth;
        _verticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, ContentHeight - Math.Max(0, ActualHeight - Header)));
        var y = Header - _verticalOffset;
        foreach (var track in Project.Tracks)
        {
            var trackH = TrackHeight(track);
            var hasClipLane = track.Clips.Count > 0 || track.Takes.Count == 0;
            var count = LaneCount(track);
            var top = y + TrackLabel;
            // Lanes share the track's height (thin with many takes) and never spill into the next track.
            var laneH = Math.Max(0, (trackH - TrackLabel - 6) / count);
            var i = 0;
            if (hasClipLane)
                lanes.Add(MakeLane(track, null, top, laneH, i++, w));
            foreach (var take in track.Takes)
                lanes.Add(MakeLane(track, take, top, laneH, i++, w));
            if (LiveRecording?.TrackId == track.Id && track.Takes.Count > 0)
                lanes.Add(MakeLane(track, null, top, laneH, i, w) with { IsLive = true });
            y += trackH;
        }
        return lanes;
    }

    private static Lane MakeLane(Track track, Take? take, double top, double laneH, int index, double w) =>
        new(track, take,
            new Rect(0, top + laneH * index + 2, w, Math.Max(1, laneH - 4)),
            new Rect(0, top + laneH * index, w, Math.Max(1, laneH)));

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 18, 21)), null, new Rect(0, 0, w, h));
        if (Project == null) return;
        if (ViewStartFrame > Math.Max(Project.LengthFrames(), _playhead))
            ViewStartFrame = 0; // a different, shorter song was opened

        var lanes = LayoutLanes();
        dc.PushClip(new RectangleGeometry(new Rect(0, Header, w, Math.Max(0, h - Header))));
        var y = Header - _verticalOffset;
        foreach (var track in Project.Tracks)
        {
            var trackH = TrackHeight(track);
            if (y + trackH > Header && y < h) DrawTrackBackground(dc, track, y, trackH, w);
            y += trackH;
        }
        DrawBarGrid(dc, w, h);
        foreach (var lane in lanes)
            if (lane.Area.Bottom > Header && lane.Area.Top < h) DrawLane(dc, lane);
        DrawEmptyHints(dc, lanes);
        dc.Pop();
        DrawRuler(dc, w);
        DrawSelection(dc, h);
        DrawLoop(dc, h);
        DrawPlayhead(dc, h);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (Project == null) return;
        var p = e.GetPosition(this);
        var frame = XToFrame(p.X);
        _dragStart = p;

        if (p.Y < Header)
        {
            _rangeDragging = true;
            _rangeStart = _rangeEnd = frame;
            CaptureMouse();
            InvalidateVisual();
            return;
        }

        var lane = HitLane(p);
        var hitTrack = lane?.Track ?? HitTrack(p);
        if (hitTrack != null)
        {
            SelectedTrackId = hitTrack.Id;
            SelectTrack?.Invoke(hitTrack.Id);
        }
        if (lane == null || lane.Value.IsLive)
        {
            InvalidateVisual();
            return;
        }
        if (lane?.Take is { } take)
        {
            SelectedClipId = take.Id;
            SelectClip?.Invoke(take.Id);
            if (CompMode)
            {
                _compTake = take;
                _compStart = _compEnd = frame;
                CaptureMouse();
            }
            else
            {
                // Takes are not dragged (beat snap would misalign them); the click places the playhead.
                Seek?.Invoke(frame);
            }
        }
        else if (lane?.Track.Clips.FirstOrDefault(c => frame >= c.StartFrame && frame < c.EndFrame) is { } clip)
        {
            SelectedClipId = clip.Id;
            SelectClip?.Invoke(clip.Id);
            _dragging = true;
            _dragClipStart = clip.StartFrame;
            CaptureMouse();
        }
        else
        {
            Seek?.Invoke(frame);
        }
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Project == null) return;
        var p = e.GetPosition(this);
        if (_rangeDragging)
        {
            _rangeEnd = XToFrame(p.X);
            InvalidateVisual();
            return;
        }
        if (_compTake != null)
        {
            _compEnd = XToFrame(p.X);
            InvalidateVisual();
            return;
        }
        if (!_dragging || SelectedClipId == null) return;
        var deltaFrames = (long)((p.X - _dragStart.X) / PixelsPerSecond * Project.SampleRate);
        MoveClip?.Invoke(SelectedClipId, Math.Max(0, _dragClipStart + deltaFrames));
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var moved = Math.Abs(e.GetPosition(this).X - _dragStart.X) > 4;
        if (_rangeDragging)
        {
            _rangeDragging = false;
            ReleaseMouseCapture();
            if (moved)
                SelectRange?.Invoke(Math.Min(_rangeStart, _rangeEnd), Math.Max(_rangeStart, _rangeEnd));
            else
            {
                SelectRange?.Invoke(0, 0); // a plain click on the ruler clears the range and seeks
                Seek?.Invoke(_rangeStart);
            }
        }
        else if (_compTake != null)
        {
            var take = _compTake;
            _compTake = null;
            ReleaseMouseCapture();
            if (moved)
                ChoosePart?.Invoke(take.Id, Math.Min(_compStart, _compEnd), Math.Max(_compStart, _compEnd));
            else
                Seek?.Invoke(_compStart);
        }
        else if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var pointerX = e.GetPosition(this).X;
            var anchor = XToFrame(pointerX);
            PixelsPerSecond = Math.Clamp(PixelsPerSecond * (e.Delta > 0 ? 1.15 : 0.87), 20, 400);
            if (Project != null)
                ViewStartFrame = Math.Max(0, anchor - (long)(pointerX / PixelsPerSecond * Project.SampleRate));
            Zoomed?.Invoke(PixelsPerSecond);
        }
        else if (Project != null && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && ContentHeight > ActualHeight - Header)
        {
            _verticalOffset = Math.Clamp(_verticalOffset - e.Delta * 0.5, 0,
                Math.Max(0, ContentHeight - Math.Max(0, ActualHeight - Header)));
        }
        else if (Project != null)
        {
            // Wheel scrolls through the song a quarter screen at a time.
            var step = (long)(ActualWidth * 0.25 / PixelsPerSecond * Project.SampleRate);
            var maxStart = Math.Max(0, Project.LengthFrames() - step);
            ViewStartFrame = Math.Clamp(ViewStartFrame - Math.Sign(e.Delta) * step, 0, maxStart);
        }
        InvalidateVisual();
        e.Handled = true;
    }

    private void FollowPlayhead()
    {
        if (Project == null || ActualWidth <= 0) return;
        var x = FrameToX(_playhead);
        if (x >= 0 && x <= ActualWidth - 24) return;
        var lead = (long)((x < 0 ? ActualWidth * 0.1 : ActualWidth - 24) / PixelsPerSecond * Project.SampleRate);
        ViewStartFrame = Math.Max(0, _playhead - lead);
    }

    private void DrawRuler(DrawingContext dc, double w)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(27, 30, 35)), null, new Rect(0, 0, w, Header));
        if (Project == null) return;
        var beat = TimelineMath.SamplesPerBeat(Project.SampleRate, Project.TempoBpm);
        var bar = TimelineMath.SamplesPerBar(Project.SampleRate, Project.TempoBpm,
            Project.TimeSignature.Numerator, Project.TimeSignature.Denominator);
        for (var frame = ViewStartFrame / beat * beat; ; frame += beat)
        {
            var x = FrameToX(frame);
            if (x > w) break;
            var isBar = frame % bar == 0;
            dc.DrawLine(new Pen(new SolidColorBrush(isBar ? Color.FromRgb(31, 145, 80) : Color.FromRgb(60, 66, 76)), isBar ? 1.5 : 1),
                new Point(x, isBar ? 8 : 16), new Point(x, Header));
            if (isBar)
            {
                var barNum = (int)(frame / bar) + 1;
                dc.DrawText(Text(barNum.ToString(), 12, Cream, UiBold), new Point(x + 4, 3));
            }
        }
    }

    private void DrawTrackBackground(DrawingContext dc, Track track, double y, double h, double w)
    {
        var bg = track.Role == TrackRole.Vocal
            ? Color.FromRgb(28, 30, 36)
            : Color.FromRgb(24, 27, 31);
        dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, y, w, h));
        if (track.Id == SelectedTrackId)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 232, 236, 241)), null, new Rect(0, y, w, h));
            dc.DrawRectangle(new SolidColorBrush(ColorFromHex(track.Color)), null, new Rect(0, y, 3, h));
        }
        dc.DrawRectangle(new SolidColorBrush(track.Armed ? Color.FromRgb(52, 26, 30) : Color.FromRgb(27, 30, 35)),
            null, new Rect(0, y, w, TrackLabel));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(42, 47, 55)), 1), new Point(0, y + h), new Point(w, y + h));

        // Header: colour swatch, name, then a red pill when this is where Record goes.
        dc.DrawRoundedRectangle(new SolidColorBrush(ColorFromHex(track.Color)), null, new Rect(10, y + 8, 9, 9), 2, 2);
        var name = Text(track.Name, 12.5, Cream, UiBold);
        dc.DrawText(name, new Point(26, y + (TrackLabel - name.Height) / 2));
        var x = 26 + name.Width + 10;
        var role = Text(track.Role == TrackRole.Vocal ? "voice" : track.Role == TrackRole.Backing ? "backing" : "audio", 11, Muted);
        dc.DrawText(role, new Point(x, y + (TrackLabel - role.Height) / 2));
        x += role.Width + 10;
        if (track.Armed)
        {
            var pill = Text("● ARMED · press R to record", 10.5, Cream, UiBold);
            var r = new Rect(x, y + 4, pill.Width + 14, TrackLabel - 8);
            dc.DrawRoundedRectangle(RecordRed, null, r, r.Height / 2, r.Height / 2);
            dc.DrawText(pill, new Point(r.X + 7, r.Y + (r.Height - pill.Height) / 2));
        }
    }

    // Faint bar lines through the lanes so takes line up against the beat by eye.
    private void DrawBarGrid(DrawingContext dc, double w, double h)
    {
        if (Project == null) return;
        var bar = TimelineMath.SamplesPerBar(Project.SampleRate, Project.TempoBpm,
            Project.TimeSignature.Numerator, Project.TimeSignature.Denominator);
        if (bar <= 0) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(22, 232, 236, 241)), 1);
        pen.Freeze();
        for (var frame = ViewStartFrame / bar * bar; ; frame += bar)
        {
            var x = Math.Round(FrameToX(frame)) + 0.5;
            if (x > w) break;
            dc.DrawLine(pen, new Point(x, Header), new Point(x, h));
        }
    }

    private void DrawLane(DrawingContext dc, Lane lane)
    {
        var track = lane.Track;
        if (lane.IsLive)
        {
            DrawLiveRecording(dc, lane.Area);
            return;
        }
        if (lane.Take is not { } take)
        {
            var color = ColorFromHex(track.Color);
            foreach (var clip in track.Clips)
            {
                color.A = clip.Id == SelectedClipId ? (byte)230 : (byte)170;
                DrawClip(dc, clip, lane.Area, color, clip.Id == SelectedClipId);
            }
            if (LiveRecording?.TrackId == track.Id && track.Takes.Count == 0)
                DrawLiveRecording(dc, lane.Area);
            return;
        }

        // The take you hear is bright; the others are dimmed. Once parts are chosen, the chosen
        // sections (copper outlines) are what plays.
        var latest = track.Takes.Where(t => t.Committed).OrderBy(t => t.RecordedUtc).LastOrDefault();
        var heard = track.Comp.Regions.Count == 0 &&
                    (track.AuditionTakeId == take.Id ||
                     (string.IsNullOrEmpty(track.AuditionTakeId) && track.Clips.Count == 0 && take == latest));
        var fake = new AudioClip
        {
            Id = take.Id,
            StartFrame = take.StartFrame,
            SourceOffsetFrames = take.SourceOffsetFrames,
            LengthFrames = take.LengthFrames
        };
        var takeColor = heard ? Color.FromRgb(22, 92, 56) : Color.FromRgb(40, 44, 52);
        DrawClip(dc, fake, lane.Area, takeColor, take.Id == SelectedClipId, take.Name, take.RelativePath, take.Id,
            heard, heard ? "you hear this one" : null);

        foreach (var region in track.Comp.Regions.Where(r => r.TakeId == take.Id))
        {
            var x1 = FrameToX(region.TimelineStartFrame);
            var x2 = FrameToX(region.EndFrame);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 232, 236, 241)),
                new Pen(new SolidColorBrush(Color.FromRgb(61, 187, 111)), 2),
                new Rect(x1, lane.Area.Y + 1, Math.Max(2, x2 - x1), Math.Max(1, lane.Area.Height - 2)));
        }

        if (_compTake?.Id == take.Id)
        {
            var x1 = FrameToX(Math.Min(_compStart, _compEnd));
            var x2 = FrameToX(Math.Max(_compStart, _compEnd));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 61, 187, 111)), null,
                new Rect(x1, lane.Area.Y, Math.Max(2, x2 - x1), lane.Area.Height));
        }
    }

    private void DrawLiveRecording(DrawingContext dc, Rect lane)
    {
        if (LiveRecording is not { } live || live.SampleRate <= 0) return;
        var startX = FrameToX(live.StartFrame);
        var endX = startX + live.LengthFrames / (double)live.SampleRate * PixelsPerSecond;
        var left = Math.Max(0, startX);
        var right = Math.Min(ActualWidth, Math.Max(startX + 3, endX));
        if (right <= left) return;
        var rect = new Rect(left, lane.Y, right - left, lane.Height);
        dc.PushClip(new RectangleGeometry(rect));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210, 133, 43, 43)),
            new Pen(Brushes.IndianRed, 1), rect, 3, 3);
        var mid = rect.Y + rect.Height * 0.6;
        var amplitude = rect.Height * 0.32;
        // Buckets are bounded by the engine and retain their relative time after compaction.
        foreach (var bucket in live.Buckets)
        {
            var x1 = startX + bucket.StartFrame / (double)live.SampleRate * PixelsPerSecond;
            var x2 = startX + (bucket.StartFrame + bucket.FrameCount) / (double)live.SampleRate * PixelsPerSecond;
            if (x2 < left || x1 > right) continue;
            var top = mid - Math.Clamp(bucket.Maximum, -1, 1) * amplitude;
            var bottom = mid - Math.Clamp(bucket.Minimum, -1, 1) * amplitude;
            dc.DrawRectangle(Brushes.MistyRose, null,
                new Rect(Math.Max(left, x1), top, Math.Max(1, Math.Min(right, x2) - Math.Max(left, x1)), Math.Max(1, bottom - top)));
        }
        var label = new FormattedText("● Recording", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point(left + 6, rect.Y + 2));
        dc.Pop();
    }

    private void DrawClip(DrawingContext dc, AudioClip clip, Rect lane, Color fill, bool selected,
        string? caption = null, string? audioPath = null, string? peakKey = null,
        bool emphasised = true, string? badge = null)
    {
        if (Project == null) return;
        var x1 = FrameToX(clip.StartFrame);
        var x2 = FrameToX(clip.EndFrame);
        var rect = new Rect(x1, lane.Y, Math.Max(3, x2 - x1), lane.Height);
        // Selection is the cream outline; a heard take gets a copper edge; dimmed takes a dark one.
        var edge = selected ? Color.FromRgb(232, 236, 241)
            : badge != null ? Color.FromRgb(61, 187, 111)
            : Color.FromRgb(10, 11, 13);
        dc.DrawRoundedRectangle(new SolidColorBrush(fill),
            new Pen(new SolidColorBrush(edge), selected || badge != null ? 1.5 : 1),
            rect, 4, 4);

        if (clip.FadeInFrames > 0)
        {
            var fx = FrameToX(clip.StartFrame + clip.FadeInFrames);
            dc.DrawLine(new Pen(Brushes.White, 1), new Point(x1, rect.Bottom), new Point(fx, rect.Y));
        }
        if (clip.FadeOutFrames > 0)
        {
            var fx = FrameToX(clip.EndFrame - clip.FadeOutFrames);
            dc.DrawLine(new Pen(Brushes.White, 1), new Point(fx, rect.Y), new Point(x2, rect.Bottom));
        }

        if (audioPath == null && !string.IsNullOrEmpty(clip.MediaId) && Project.FindMedia(clip.MediaId) is { } media)
        {
            audioPath = media.WorkingRelativePath;
            peakKey = media.Id;
        }
        if (audioPath != null && peakKey != null && Project.RootPath != "" && rect.Height >= 8)
        {
            var peaks = LoadPeaks(audioPath, peakKey);
            if (peaks != null)
                DrawPeaks(dc, peaks, clip, rect, emphasised ? (byte)215 : (byte)110);
        }

        if (!string.IsNullOrEmpty(caption) && rect.Height >= 16)
        {
            dc.PushClip(new RectangleGeometry(rect));
            var text = Text(caption, 11.5, emphasised ? Cream : Muted, UiBold);
            var cx = Math.Max(rect.X, 0) + 7;
            dc.DrawText(text, new Point(cx, rect.Y + 3));
            if (badge != null && rect.Height >= 20)
            {
                var b = Text("▶ " + badge, 10.5, new SolidColorBrush(Color.FromRgb(18, 20, 23)), UiBold);
                var r = new Rect(cx + text.Width + 8, rect.Y + 3, b.Width + 12, Math.Max(text.Height, b.Height));
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(61, 187, 111)), null, r, r.Height / 2, r.Height / 2);
                dc.DrawText(b, new Point(r.X + 6, r.Y + (r.Height - b.Height) / 2));
            }
            dc.Pop();
        }
    }

    // Waveforms are built off the UI thread (a long take is a lot of WAV to read); the lane draws
    // without one until it's ready.
    private PeakData? LoadPeaks(string audioPath, string key)
    {
        if (_peaks.TryGetValue(key, out var cached)) return cached;
        if (_noPeaks.Contains(key) || !_building.Add(key)) return null;
        var paths = new Core.Persistence.ProjectPaths(Project!.RootPath);
        Task.Run(() =>
        {
            PeakData? peaks = null;
            try { peaks = _waves.LoadOrBuild(paths, audioPath, key); }
            catch { /* cache is disposable; a missing or unreadable file just draws without a waveform */ }
            Dispatcher.InvokeAsync(() =>
            {
                _building.Remove(key);
                if (peaks != null) _peaks[key] = peaks;
                else _noPeaks.Add(key);
                InvalidateVisual();
            });
        });
        return null;
    }

    // One line per visible pixel column (the min/max of the buckets under it), not one per bucket:
    // a 3-minute take is ~34,000 buckets and the timeline redraws 20 times a second.
    private void DrawPeaks(DrawingContext dc, PeakData peaks, AudioClip clip, Rect rect, byte alpha = 200)
    {
        if (peaks.Hop <= 0 || peaks.Max.Length == 0 || rect.Width <= 0) return;
        var startBucket = clip.SourceOffsetFrames / (double)peaks.Hop;
        var bucketsPerPixel = clip.LengthFrames / (double)peaks.Hop / rect.Width;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 243, 230, 212)), 1);
        pen.Freeze();
        var mid = rect.Y + rect.Height / 2;
        var amp = rect.Height * 0.42;
        var fromX = Math.Max(0, (int)Math.Floor(rect.X));
        var toX = Math.Min((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(rect.Right));
        for (var x = fromX; x < toX; x++)
        {
            var b0 = (int)(startBucket + (x - rect.X) * bucketsPerPixel);
            var b1 = Math.Max(b0 + 1, (int)(startBucket + (x + 1 - rect.X) * bucketsPerPixel));
            var hi = -1f;
            var lo = 1f;
            for (var b = Math.Max(0, b0); b < Math.Min(b1, peaks.Max.Length); b++)
            {
                hi = Math.Max(hi, peaks.Max[b]);
                lo = Math.Min(lo, peaks.Min[b]);
            }
            if (hi < lo) continue;
            dc.DrawLine(pen, new Point(x, mid - hi * amp), new Point(x, mid - lo * amp));
        }
    }

    // Each empty track says what goes there, inside a dashed drop zone, instead of a blank board.
    private void DrawEmptyHints(DrawingContext dc, List<Lane> lanes)
    {
        if (Project == null) return;
        var dash = new Pen(new SolidColorBrush(Color.FromRgb(58, 64, 74)), 1) { DashStyle = new DashStyle([4, 4], 0) };
        dash.Freeze();
        foreach (var lane in lanes)
        {
            var track = lane.Track;
            if (lane.Take != null || lane.IsLive || HasContent(track)) continue;
            var zone = new Rect(lane.Area.X + 10, lane.Area.Y + 4, Math.Max(0, lane.Area.Width - 20), Math.Max(0, lane.Area.Height - 10));
            if (zone.Height < 18 || zone.Width < 60) continue;
            dc.DrawRoundedRectangle(null, dash, zone, 4, 4);
            var hint = track.Role == TrackRole.Backing
                ? "Drop a WAV or MP3 backing track here, or click Import WAV / MP3"
                : track.Armed
                    ? "Ready · press R (or Record) to record here"
                    : "Empty · click this track, then Arm selected to record here";
            var text = Text(hint, 13, Muted);
            text.TextAlignment = TextAlignment.Center;
            text.MaxTextWidth = Math.Max(60, zone.Width - 20);
            text.MaxLineCount = 2;
            dc.DrawText(text, new Point(zone.X + 10, zone.Y + (zone.Height - text.Height) / 2));
        }
    }

    private void DrawSelection(DrawingContext dc, double h)
    {
        if (Project == null) return;
        long start, end;
        if (_rangeDragging)
            (start, end) = (Math.Min(_rangeStart, _rangeEnd), Math.Max(_rangeStart, _rangeEnd));
        else
            (start, end) = (Project.SelectionStartFrame, Project.SelectionEndFrame);
        if (end <= start) return;
        var x1 = FrameToX(start);
        var x2 = FrameToX(end);
        var width = Math.Max(2, x2 - x1);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 61, 187, 111)), null, new Rect(x1, 0, width, Header));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(26, 232, 236, 241)), null, new Rect(x1, Header, width, h - Header));
    }

    private void DrawPlayhead(DrawingContext dc, double h)
    {
        var x = FrameToX(Playhead);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(232, 236, 241)), 1.5),
            new Point(x, 0), new Point(x, h));
        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(new Point(x - 6, 0), true, true);
            ctx.LineTo(new Point(x + 6, 0), true, false);
            ctx.LineTo(new Point(x, Header - 4), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(229, 72, 77)), null, tri);
    }

    private void DrawLoop(DrawingContext dc, double h)
    {
        if (Project?.Loop.Enabled != true) return;
        var x1 = FrameToX(Project.Loop.StartFrame);
        var x2 = FrameToX(Project.Loop.EndFrame);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 31, 145, 80)), null,
            new Rect(x1, Header, Math.Max(2, x2 - x1), h - Header));
    }

    private Lane? HitLane(Point p)
    {
        foreach (var lane in LayoutLanes())
            if (p.Y >= lane.Slot.Top && p.Y < lane.Slot.Bottom)
                return lane;
        return null;
    }

    private Track? HitTrack(Point p)
    {
        if (Project == null || p.Y < Header) return null;
        var y = Header - _verticalOffset;
        foreach (var track in Project.Tracks)
        {
            var height = TrackHeight(track);
            if (p.Y >= y && p.Y < y + height) return track;
            y += height;
        }
        return null;
    }

    private double FrameToX(long frame) =>
        Project == null ? 0 : (frame - ViewStartFrame) / (double)Project.SampleRate * PixelsPerSecond;

    private long XToFrame(double x) =>
        Project == null ? 0 : (long)Math.Max(0, ViewStartFrame + x / PixelsPerSecond * Project.SampleRate);

    private FormattedText Text(string s, double size, Brush brush, Typeface? face = null) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face ?? Ui, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color ColorFromHex(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex)!;
            return c;
        }
        catch
        {
            return Color.FromRgb(31, 145, 80);
        }
    }
}
