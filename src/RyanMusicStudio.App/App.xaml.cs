using System.Windows;
using System.Windows.Threading;

namespace RyanMusicStudio.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message + Environment.NewLine + Environment.NewLine +
            "Your finished takes stay on disk. Try Audio Setup if a device changed.",
            "RMS — something stopped",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
