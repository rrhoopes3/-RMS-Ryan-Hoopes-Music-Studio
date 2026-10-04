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
        if (!double.TryParse(TempoBox.Text, out var tempo) || !double.IsFinite(tempo) || tempo < 40 || tempo > 240)
        {
            MessageBox.Show(this, "Choose a tempo from 40 to 240 beats per minute.", "RMS");
            return;
        }
        if (!int.TryParse(NumBox.Text, out var numerator) || numerator < 1 || numerator > 32 ||
            !int.TryParse(DenBox.Text, out var denominator) || denominator is not (1 or 2 or 4 or 8 or 16 or 32))
        {
            MessageBox.Show(this, "Choose 1–32 beats and a note value of 1, 2, 4, 8, 16, or 32.", "RMS");
            return;
        }
        try { Directory.CreateDirectory(Folder); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not use that folder: " + ex.Message, "RMS");
            return;
        }
        DialogResult = true;
    }
}
