namespace RyanMusicStudio.Core.Model;

public static class ProjectFactory
{
    public static readonly string[] TrackColors =
    [
        "#C17F45", "#B83232", "#6E8B6C", "#D4A574", "#4F6F8F", "#8A6A4B", "#C9B27C"
    ];

    public static ProjectDocument CreateVocalOverBeat(
        string name,
        string folder,
        double tempoBpm,
        int numerator,
        int denominator,
        int sampleRate)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name is required.", nameof(name));
        if (!ProjectDocument.IsSupportedSampleRate(sampleRate))
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Choose 44.1 kHz or 48 kHz.");
        if (tempoBpm is < 20 or > 300)
            throw new ArgumentOutOfRangeException(nameof(tempoBpm));

        var root = Path.GetFullPath(folder);
        var project = new ProjectDocument
        {
            Name = name.Trim(),
            RootPath = root,
            SampleRate = sampleRate,
            TempoBpm = tempoBpm,
            TimeSignature = new TimeSignature { Numerator = numerator, Denominator = denominator },
            CountInBars = 1,
            PreRollBars = 1,
            Dirty = true
        };

        project.Tracks.Add(new Track
        {
            Name = "Backing track",
            Role = TrackRole.Backing,
            Color = "#C17F45",
            Channels = TrackChannelLayout.Stereo,
            Armed = false
        });

        project.Tracks.Add(new Track
        {
            Name = "Vocal",
            Role = TrackRole.Vocal,
            Color = "#B83232",
            Channels = TrackChannelLayout.Mono,
            Armed = true
        });

        project.Markers.Add(new Marker
        {
            Name = "Verse",
            Frame = 0,
            Lyrics = "Write the words you want to see while you sing."
        });

        return project;
    }

    public static Track CreateAudioTrack(ProjectDocument project, string name, TrackRole role, TrackChannelLayout channels)
    {
        var color = TrackColors[project.Tracks.Count % TrackColors.Length];
        var track = new Track
        {
            Name = name,
            Role = role,
            Color = color,
            Channels = channels,
            Armed = role == TrackRole.Vocal
        };
        project.Tracks.Add(track);
        project.Touch();
        return track;
    }
}
