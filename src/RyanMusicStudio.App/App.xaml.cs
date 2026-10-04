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
            "Your song stays on this computer. Close RMS and open it again if this keeps happening.",
            "RMS — something stopped",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
