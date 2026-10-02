#if PORTABLE
using System.Diagnostics;
#else
using NAudio.MediaFoundation;
#endif
using NAudio.Wave;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Timeline;
using RyanMusicStudio.Engine.IO;
using RyanMusicStudio.Engine.Media;
using RyanMusicStudio.Engine.Mix;

namespace RyanMusicStudio.Engine.Export;

public sealed class MixExporter
{
    public void Export(
        ProjectDocument project,
        SampleCache cache,
        string destination,
        ExportFormat format,
        ExportScope scope,
        long startFrame,
        long endFrame)
    {
        if (endFrame <= startFrame)
            endFrame = Math.Max(project.SampleRate, project.LengthFrames());
        startFrame = TimelineMath.ClampNonNegative(startFrame);

        var mixer = new ProjectMixer(project.SampleRate) { ExportMode = true };
        var filtered = FilterForScope(project, scope);
        cache.PreloadProject(filtered);
        mixer.SetSnapshot(ProjectMixer.Build(filtered, cache, metronome: false, softwareMonitor: false, 0));
        mixer.SetPlayhead(startFrame);

        var frames = endFrame - startFrame;
        var stereo = new float[frames * 2];
        var read = 0;
        var chunk = new float[project.SampleRate * 2];
        while (read < stereo.Length)
        {
            var want = Math.Min(chunk.Length, stereo.Length - read);
            var got = mixer.Read(chunk, 0, want);
            if (got <= 0) break;
            Array.Copy(chunk, 0, stereo, read, got);
            read += got;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (format == ExportFormat.Mp3)
        {
            var tempWav = destination + ".tmp.wav";
            WritePcmWav(tempWav, stereo, project.SampleRate, 16);
            try
            {
#if PORTABLE
                var psi = new ProcessStartInfo("ffmpeg")
                {
                    UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true
                };
                foreach (var arg in new[] { "-nostdin", "-v", "error", "-y", "-i", tempWav,
                             "-codec:a", "libmp3lame", "-b:a", "192k", destination })
                    psi.ArgumentList.Add(arg);
                Process process;
                try { process = Process.Start(psi) ?? throw new InvalidOperationException("FFmpeg did not start."); }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new InvalidOperationException("FFmpeg is required for MP3 export on macOS and Linux. Install ffmpeg and reopen RMS.", ex);
                }
                using (process)
                {
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("MP3 export failed: " + error);
                }
#else
                MediaFoundationApi.Startup();
                using var reader = new MediaFoundationReader(tempWav);
                MediaFoundationEncoder.EncodeToMp3(reader, destination, 192000);
#endif
            }
            finally
            {
                try { File.Delete(tempWav); } catch { /* ignore */ }
            }
        }
        else
        {
            var bits = format == ExportFormat.Wav24 ? 24 : 16;
            WritePcmWav(destination, stereo, project.SampleRate, bits);
        }
    }

    private static ProjectDocument FilterForScope(ProjectDocument project, ExportScope scope)
    {
        var cloneJson = Core.Editing.ProjectSerializer.ToJson(project);
        var clone = Core.Editing.ProjectSerializer.FromJson(cloneJson, project.RootPath);
        var keep = new HashSet<string>(clone.ExportableTracks(scope).Select(t => t.Id));
        clone.Tracks.RemoveAll(t => !keep.Contains(t.Id));
        return clone;
    }

    private static void WritePcmWav(string path, float[] interleaved, int sampleRate, int bits)
    {
        var format = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, sampleRate, 2, sampleRate * 2 * (bits / 8), 2 * (bits / 8), bits);
        using var writer = new WaveFileWriter(path, format);
        if (bits == 16)
        {
            for (var i = 0; i < interleaved.Length; i++)
            {
                var s = Math.Clamp(interleaved[i], -1f, 1f);
                writer.WriteSample(s);
            }
        }
        else
        {
            for (var i = 0; i < interleaved.Length; i++)
            {
                var s = Math.Clamp(interleaved[i], -1f, 1f);
                var v = (int)Math.Round(s * 8388607.0);
                writer.WriteByte((byte)(v & 0xFF));
                writer.WriteByte((byte)((v >> 8) & 0xFF));
                writer.WriteByte((byte)((v >> 16) & 0xFF));
            }
        }
    }
}
