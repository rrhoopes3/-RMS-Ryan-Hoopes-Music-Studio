using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using Xunit;

namespace RyanMusicStudio.Tests;

public sealed class ProjectFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rms-folders-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("..", "New song")]
    [InlineData("  My song.  ", "My song")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.wav", "_nul.wav")]
    [InlineData("LPT1", "_LPT1")]
    [InlineData("Verse / chorus: take?", "Verse - chorus- take-")]
    public void Folder_names_can_move_between_Windows_Mac_and_Linux(string name, string expected) =>
        Assert.Equal(expected, ProjectPaths.SafeFolderName(name));

    [Fact]
    public void Save_as_copies_media_and_keeps_original_song_intact()
    {
        var store = new ProjectStore();
        var source = Path.Combine(_root, "original");
        var dest = Path.Combine(_root, "copy");
        var project = ProjectFactory.CreateVocalOverBeat("Song", source, 90, 4, 4, 48000);
        store.Save(project);
        var media = Path.Combine(new ProjectPaths(source).TakesDir, "take.wav");
        File.WriteAllText(media, "audio bytes");
        project.Name = "Edited song";
        project.Touch();
        store.SaveAs(project, dest);
        Assert.Equal(dest, project.RootPath);
        Assert.False(project.Dirty);
        Assert.Equal("Song", store.Open(source).Name);
        Assert.Equal("Edited song", store.Open(dest).Name);
        Assert.Equal("audio bytes", File.ReadAllText(Path.Combine(new ProjectPaths(dest).TakesDir, "take.wav")));
    }

    [Fact]
    public void Save_as_refuses_nonempty_folder_without_changing_the_open_song()
    {
        var store = new ProjectStore();
        var source = Path.Combine(_root, "original");
        var dest = Path.Combine(_root, "copy");
        var project = ProjectFactory.CreateVocalOverBeat("Song", source, 90, 4, 4, 48000);
        store.Save(project);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "keep.txt"), "keep me");
        project.Touch();
        Assert.Throws<IOException>(() => store.SaveAs(project, dest));
        Assert.Equal(source, project.RootPath);
        Assert.True(project.Dirty);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(dest, "keep.txt")));
        Assert.Single(Directory.GetFileSystemEntries(dest));
    }

    [Fact]
    public void Failed_save_as_keeps_original_root_and_unsaved_edits()
    {
        var store = new ProjectStore();
        var source = Path.Combine(_root, "original");
        var dest = Path.Combine(_root, "copy");
        var project = ProjectFactory.CreateVocalOverBeat("Song", source, 90, 4, 4, 48000);
        store.Save(project);
        project.Master.GainDb = double.NaN; // serialization fails after the media copy
        project.Touch();
        Assert.Throws<ArgumentException>(() => store.SaveAs(project, dest));
        Assert.Equal(source, project.RootPath);
        Assert.True(project.Dirty);
        Assert.Equal("Song", store.Open(source).Name);
        Assert.False(File.Exists(new ProjectPaths(dest).ProjectFile));
    }

    [Theory]
    [InlineData(double.NaN, 4, 4)]
    [InlineData(double.PositiveInfinity, 4, 4)]
    [InlineData(90, 0, 4)]
    [InlineData(90, 4, 0)]
    [InlineData(90, 4, 3)]
    public void Invalid_timing_is_rejected_before_creating_a_song(double bpm, int beats, int note) =>
        Assert.ThrowsAny<ArgumentException>(() => ProjectFactory.CreateVocalOverBeat("Song", _root, bpm, beats, note, 48000));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
