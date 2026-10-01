using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace RyanMusicStudio.App;

public partial class NewProjectWindow : Window
{
    public string ProjectName => NameBox.Text.Trim();
    public string Folder => FolderBox.Text.Trim();
    public double Tempo => double.TryParse(TempoBox.Text, out var t) ? t : 90;
    public int Numerator => int.TryParse(NumBox.Text, out var n) ? n : 4;
    public int Denominator => int.TryParse(DenBox.Text, out var d) ? d : 4;
    public int SampleRate => RateBox.SelectedIndex == 1 ? 44100 : 48000;

    public NewProjectWindow(string? suggestedParent)
    {
        InitializeComponent();
        FolderBox.Text = string.IsNullOrWhiteSpace(suggestedParent)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RMS Projects")
            : suggestedParent;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder for this RMS session" };
        if (dlg.ShowDialog() == true)
            FolderBox.Text = dlg.FolderName;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            MessageBox.Show("Give the session a name.", "RMS");
            return;
        }
        if (string.IsNullOrWhiteSpace(Folder))
        {
            MessageBox.Show("Choose a folder.", "RMS");
            return;
        }
        Directory.CreateDirectory(Folder);
        DialogResult = true;
    }
}
