using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RyanMusicStudio.App;
using RyanMusicStudio.Engine.Media;

// Runs on a Windows CI desktop, using the real WPF templates and synthesized sample audio.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "dist/windows-ui");
        Directory.CreateDirectory(output);
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow();
        window.Show();
        Capture(window, Path.Combine(output, "home.png"));

        var session = (SessionController)typeof(MainWindow)
            .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var projectRoot = Path.Combine(Path.GetTempPath(), "rms-ui-smoke-" + Guid.NewGuid().ToString("N"));
        SampleProjectBuilder.Create(projectRoot);
        if (!session.TryOpen(projectRoot)) throw new InvalidOperationException("The sample song did not open.");
        foreach (var place in new[] { StudioPlace.Arrange, StudioPlace.Mix, StudioPlace.Export })
        {
            session.Go(place);
            Capture(window, Path.Combine(output, place.ToString().ToLowerInvariant() + ".png"));
        }
        session.Go(StudioPlace.Arrange);
        window.Width = 980;
        window.Height = 640;
        Capture(window, Path.Combine(output, "record-small-window.png"));
        var meter = (FrameworkElement)window.FindName("InMeter");
        if (meter.ActualWidth < 100) throw new InvalidOperationException("The transport crowded out the input meter.");

        var dialog = new NewProjectWindow(projectRoot) { Owner = window };
        dialog.Show();
        Capture(dialog, Path.Combine(output, "new-session.png"));
        var rate = (FrameworkElement)dialog.FindName("RateBox");
        var right = rate.TranslatePoint(new Point(rate.ActualWidth, 0), dialog).X;
        if (right > dialog.ActualWidth - 16) throw new InvalidOperationException("Sample-rate choice is clipped.");
        dialog.Close();
        window.Close();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        app.Shutdown();
        Console.WriteLine("Windows UI smoke checks passed; screenshots: " + output);
    }

    private static void Capture(Window window, string path)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        png.Save(file);
    }
}
