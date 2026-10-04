using System.Reflection;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Engine.Audio;
using RyanMusicStudio.Engine.IO;
using Xunit;

namespace RyanMusicStudio.Tests;

public class RecordingRecoveryTests
{
    [Fact]
    public void Recovery_offers_an_autosave_written_less_than_one_second_after_save()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore();
            var project = ProjectFactory.CreateVocalOverBeat("Saved", root, 90, 4, 4, 48000);
            store.Save(project);
            project.Name = "Autosaved";
            project.Touch();
            store.Autosave(project);

            var paths = new ProjectPaths(root);
            var savedTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(paths.ProjectFile, savedTime);
            File.SetLastWriteTimeUtc(paths.AutosaveFile, savedTime.AddMilliseconds(500));

            var offer = store.InspectRecovery(root);
            Assert.NotNull(offer);
            Assert.True(offer.AutosaveIsNewer);
            Assert.Equal("Autosaved", store.OpenAutosave(root).Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Stopping_a_take_drains_samples_queued_before_the_writer_stops()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-tail-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var engine = new AudioEngine();
            var project = ProjectFactory.CreateVocalOverBeat("Tail", root, 90, 4, 4, 48000);
            engine.AttachProject(project);
            var track = project.Tracks.First(t => t.Role == TrackRole.Vocal);
            var paths = new ProjectPaths(root);
            paths.EnsureLayout();
            var path = Path.Combine(paths.TakesDir, "tail.wav");
            var writer = new IncrementalWavWriter(path, project.SampleRate, 1);
            var cts = new CancellationTokenSource();
            var ring = (FloatRingBuffer)Field("_captureRing").GetValue(engine)!;
            var waveform = new RecordingWaveform(track.Id, 0, project.SampleRate);
            const int tailFrames = 4800;
            Assert.Equal(tailFrames, ring.Write(Enumerable.Repeat(0.25f, tailFrames).ToArray()));

            // Hold the writer until StopRecordAsync has cancelled it. The old loop exited on
            // cancellation without reading this tail, and the old finalizer cleared _writer early.
            var loop = typeof(AudioEngine).GetMethod("WriterLoop", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var writerTask = Task.Run(() =>
            {
                if (!SpinWait.SpinUntil(() => cts.IsCancellationRequested, TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The take writer was never asked to stop.");
                loop.Invoke(engine, [ring, writer, waveform, cts.Token]);
            });

            Field("_armedTrack").SetValue(engine, track);
            Field("_writer").SetValue(engine, writer);
            Field("_writerCts").SetValue(engine, cts);
            Field("_writerTask").SetValue(engine, writerTask);
            Field("_inProgressTakePath").SetValue(engine, path);
            Field("_recording").SetValue(engine, true);
            Field("_takeActive").SetValue(engine, true);
            new ProjectStore().MarkRecording(root, path);

            var result = await engine.StopRecordAsync();
            Assert.NotNull(result);
            Assert.Equal(tailFrames, result.Take.LengthFrames);
            Assert.False(File.Exists(paths.RecordingMarker));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Exit_after_failed_take_keeps_the_recording_marker_for_recovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-failed-take-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new ProjectPaths(root);
            new ProjectStore().MarkRecording(root, Path.Combine(paths.TakesDir, "unfinished.wav"));
            var engine = new AudioEngine();
            Field("_takeFinalizationError").SetValue(engine, new IOException("Writer failed"));

            engine.DisposePreservingRecovery();

            Assert.True(File.Exists(paths.RecordingMarker));
            Assert.NotNull(new ProjectStore().InspectRecovery(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static FieldInfo Field(string name) =>
        typeof(AudioEngine).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
}
