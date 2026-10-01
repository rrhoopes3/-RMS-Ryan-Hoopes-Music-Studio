using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Timeline;
using RyanMusicStudio.Engine.Media;

namespace RyanMusicStudio.App.Controls;

public sealed class TimelinePanel : FrameworkElement
{
    private readonly WaveformCache _waves = new();
    private readonly Dictionary<string, PeakData> _peaks = new();
    private Point _dragStart;
    private long _dragClipStart;
    private bool _dragging;

    public ProjectDocument? Project { get; set; }
    public long Playhead { get; set; }
    public double PixelsPerSecond { get; set; } = 80;
    public string? SelectedClipId { get; set; }
    public Action<long>? Seek { get; set; }
    public Action<string, long>? MoveClip { get; set; }
    public Action<string>? SelectClip { get; set; }

    public TimelinePanel()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public void InvalidateProject() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 17, 14)), null, new Rect(0, 0, w, h));
        if (Project == null) return;

        var header = 28.0;
        DrawRuler(dc, w, header);
        var trackH = Math.Max(72, (h - header) / Math.Max(1, Project.Tracks.Count));
        var y = header;
        foreach (var track in Project.Tracks)
        {
            DrawTrack(dc, track, y, trackH, w);
            y += trackH;
        }
        DrawPlayhead(dc, header, h);
        DrawLoop(dc, header, h);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (Project == null) return;
        var p = e.GetPosition(this);
        var frame = XToFrame(p.X);
        var clip = HitClip(p);
        if (clip != null)
        {
            SelectedClipId = clip.Id;
            SelectClip?.Invoke(clip.Id);
            _dragging = true;
            _dragStart = p;
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
        if (!_dragging || Project == null || SelectedClipId == null) return;
        var dx = e.GetPosition(this).X - _dragStart.X;
        var deltaFrames = (long)(dx / PixelsPerSecond * Project.SampleRate);
        MoveClip?.Invoke(SelectedClipId, Math.Max(0, _dragClipStart + deltaFrames));
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        PixelsPerSecond = Math.Clamp(PixelsPerSecond * (e.Delta > 0 ? 1.15 : 0.87), 20, 400);
        InvalidateVisual();
    }

    private void DrawRuler(DrawingContext dc, double w, double header)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(42, 32, 24)), null, new Rect(0, 0, w, header));
        if (Project == null) return;
        var beat = TimelineMath.SamplesPerBeat(Project.SampleRate, Project.TempoBpm);
        var bar = TimelineMath.SamplesPerBar(Project.SampleRate, Project.TempoBpm,
            Project.TimeSignature.Numerator, Project.TimeSignature.Denominator);
        var type = new Typeface("Segoe UI");
        for (long frame = 0; ; frame += beat)
        {
            var x = FrameToX(frame);
            if (x > w) break;
            var isBar = frame % bar == 0;
            dc.DrawLine(new Pen(new SolidColorBrush(isBar ? Color.FromRgb(196, 132, 74) : Color.FromRgb(80, 64, 52)), isBar ? 1.5 : 1),
                new Point(x, isBar ? 8 : 16), new Point(x, header));
            if (isBar)
            {
                var barNum = (int)(frame / bar) + 1;
                var text = new FormattedText(barNum.ToString(), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, type, 11, new SolidColorBrush(Color.FromRgb(185, 168, 148)), 1.25);
                dc.DrawText(text, new Point(x + 4, 4));
            }
        }
    }

    private void DrawTrack(DrawingContext dc, Track track, double y, double h, double w)
    {
        var bg = track.Role == TrackRole.Vocal
            ? Color.FromRgb(48, 28, 26)
            : Color.FromRgb(36, 30, 24);
        dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, y, w, h));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(63, 50, 40)), 1), new Point(0, y + h), new Point(w, y + h));

        var label = new FormattedText(track.Name + (track.Armed ? "  · armed" : ""),
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, new SolidColorBrush(ColorFromHex(track.Color)), 1.25);
        dc.DrawText(label, new Point(8, y + 6));

        foreach (var clip in track.Clips)
            DrawClip(dc, clip, y + 24, h - 32, ColorFromHex(track.Color), clip.Id == SelectedClipId);

        foreach (var take in track.Takes)
        {
            var fake = new AudioClip
            {
                Id = take.Id,
                StartFrame = take.StartFrame,
                LengthFrames = take.LengthFrames
            };
            DrawClip(dc, fake, y + 24, h - 32, Color.FromRgb(180, 70, 70), false, take.Name);
        }

        foreach (var region in track.Comp.Regions)
        {
            var x1 = FrameToX(region.TimelineStartFrame);
            var x2 = FrameToX(region.EndFrame);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 243, 230, 212)), null,
                new Rect(x1, y + 24, Math.Max(2, x2 - x1), h - 32));
        }
    }

    private void DrawClip(DrawingContext dc, AudioClip clip, double y, double h, Color color, bool selected, string? caption = null)
    {
        if (Project == null) return;
        var x1 = FrameToX(clip.StartFrame);
        var x2 = FrameToX(clip.EndFrame);
        var rect = new Rect(x1, y, Math.Max(3, x2 - x1), h);
        var fill = color;
        fill.A = selected ? (byte)230 : (byte)170;
        dc.DrawRoundedRectangle(new SolidColorBrush(fill),
            new Pen(new SolidColorBrush(selected ? Colors.White : Color.FromRgb(30, 20, 16)), selected ? 2 : 1),
            rect, 3, 3);

        if (clip.FadeInFrames > 0)
        {
            var fx = FrameToX(clip.StartFrame + clip.FadeInFrames);
            dc.DrawLine(new Pen(Brushes.White, 1), new Point(x1, y + h), new Point(fx, y));
        }
        if (clip.FadeOutFrames > 0)
        {
            var fx = FrameToX(clip.EndFrame - clip.FadeOutFrames);
            dc.DrawLine(new Pen(Brushes.White, 1), new Point(fx, y), new Point(x2, y + h));
        }

        if (!string.IsNullOrEmpty(clip.MediaId) && Project.RootPath != "")
        {
            try
            {
                if (!_peaks.TryGetValue(clip.MediaId, out var peaks))
                {
                    var media = Project.FindMedia(clip.MediaId);
                    if (media != null)
                    {
                        var paths = new Core.Persistence.ProjectPaths(Project.RootPath);
                        peaks = _waves.LoadOrBuild(paths, media.WorkingRelativePath, media.Id);
                        _peaks[clip.MediaId] = peaks;
                    }
                }
                if (peaks != null)
                    DrawPeaks(dc, peaks, clip, rect);
            }
            catch
            {
                // Cache is disposable; skip drawing if a peak file is mid-write.
            }
        }

        if (!string.IsNullOrEmpty(caption))
        {
            var text = new FormattedText(caption, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, Brushes.White, 1.25);
            dc.DrawText(text, new Point(rect.X + 6, rect.Y + 4));
        }
    }

    private static void DrawPeaks(DrawingContext dc, PeakData peaks, AudioClip clip, Rect rect)
    {
        if (peaks.Hop <= 0 || peaks.Max.Length == 0) return;
        var startBucket = (int)(clip.SourceOffsetFrames / peaks.Hop);
        var buckets = (int)Math.Max(1, clip.LengthFrames / peaks.Hop);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 243, 230, 212)), 1);
        var mid = rect.Y + rect.Height / 2;
        var amp = rect.Height * 0.42;
        var last = new Point(rect.X, mid);
        for (var i = 0; i < buckets; i++)
        {
            var b = Math.Clamp(startBucket + i, 0, peaks.Max.Length - 1);
            var x = rect.X + rect.Width * i / buckets;
            var hi = mid - peaks.Max[b] * amp;
            var lo = mid - peaks.Min[b] * amp;
            dc.DrawLine(pen, new Point(x, hi), new Point(x, lo));
            last = new Point(x, hi);
        }
        _ = last;
    }

    private void DrawPlayhead(DrawingContext dc, double top, double h)
    {
        var x = FrameToX(Playhead);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(243, 230, 212)), 1.5),
            new Point(x, 0), new Point(x, h));
        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(new Point(x - 6, 0), true, true);
            ctx.LineTo(new Point(x + 6, 0), true, false);
            ctx.LineTo(new Point(x, top - 4), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(196, 60, 60)), null, tri);
    }

    private void DrawLoop(DrawingContext dc, double top, double h)
    {
        if (Project?.Loop.Enabled != true) return;
        var x1 = FrameToX(Project.Loop.StartFrame);
        var x2 = FrameToX(Project.Loop.EndFrame);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 196, 132, 74)), null,
            new Rect(x1, top, Math.Max(2, x2 - x1), h - top));
    }

    private AudioClip? HitClip(Point p)
    {
        if (Project == null) return null;
        var header = 28.0;
        var trackH = Math.Max(72, (ActualHeight - header) / Math.Max(1, Project.Tracks.Count));
        var index = (int)((p.Y - header) / trackH);
        if (index < 0 || index >= Project.Tracks.Count) return null;
        var frame = XToFrame(p.X);
        return Project.Tracks[index].Clips.FirstOrDefault(c => frame >= c.StartFrame && frame < c.EndFrame);
    }

    private double FrameToX(long frame) =>
        Project == null ? 0 : frame / (double)Project.SampleRate * PixelsPerSecond;

    private long XToFrame(double x) =>
        Project == null ? 0 : (long)Math.Max(0, x / PixelsPerSecond * Project.SampleRate);

    private static Color ColorFromHex(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex)!;
            return c;
        }
        catch
        {
            return Color.FromRgb(196, 132, 74);
        }
    }
}
