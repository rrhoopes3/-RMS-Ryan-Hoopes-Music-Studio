using System.Text.Json;

namespace RyanMusicStudio.Core.Persistence;

public sealed class RecentProject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string OpenedUtc { get; set; } = "";
}

public sealed class UserSettings
{
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public bool ExclusiveMode { get; set; }
    public bool SoftwareMonitor { get; set; }
    public int PreferredSampleRate { get; set; } = 48000;
    public int BufferMilliseconds { get; set; } = 20;
    public long UserRecordingOffsetFrames { get; set; }
    public bool AudioSetupConfirmed { get; set; }
    public string LastProjectParent { get; set; } = "";
    public List<RecentProject> Recent { get; set; } = [];

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RMS", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<UserSettings>(json, Options()) ?? new UserSettings();
            }
        }
        catch
        {
            // Keep defaults if the settings file is damaged.
        }
        return new UserSettings();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options()));
    }

    public void RememberProject(string name, string path)
    {
        Recent.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, new RecentProject
        {
            Name = name,
            Path = path,
            OpenedUtc = DateTimeOffset.UtcNow.ToString("O")
        });
        if (Recent.Count > 12)
            Recent.RemoveRange(12, Recent.Count - 12);
        LastProjectParent = Directory.GetParent(path)?.FullName ?? LastProjectParent;
        Save();
    }

    private static JsonSerializerOptions Options() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
