#if PORTABLE
using System.Diagnostics;
#else
using NAudio.MediaFoundation;
#endif
using NAudio.Wave;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
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
        long endFrame,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (startFrame < 0 || endFrame <= startFrame)
            throw new InvalidOperationException("There is no audio range to export. Record or import audio, or mark a valid range first.");

        destination = Path.GetFullPath(destination);
        var paths = new ProjectPaths(project.RootPath);
        var relative = Path.GetRelativePath(paths.Root, destination).Replace('\\', '/');
        if (relative.StartsWith("media/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("autosave/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("recovery/", StringComparison.OrdinalIgnoreCase) ||
            relative.Equals("project.json", StringComparison.OrdinalIgnoreCase) ||
            relative.Equals("project.json.bak", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a destination outside the song's media and recovery folders so its recordings stay safe.");

        var mixer = new ProjectMixer(project.SampleRate) { ExportMode = true };
        var filtered = FilterForScope(project, scope);
        cache.PreloadProject(filtered);
        mixer.SetSnapshot(ProjectMixer.Build(filtered, cache, metronome: false, softwareMonitor: false, 0));
        mixer.SetPlayhead(startFrame);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // Render beside the destination and replace it only after a complete, successful export.
        var tempBase = destination + "." + Guid.NewGuid().ToString("N");
        var tempWav = tempBase + ".tmp.wav";
        var tempMp3 = tempBase + ".tmp.mp3";
        try
        {
            RenderWav(tempWav, mixer, endFrame - startFrame, project.SampleRate,
                format == ExportFormat.Wav24 ? 24 : 16, cancellationToken,
                value => progress?.Report(format == ExportFormat.Mp3 ? value * 0.9 : value * 0.99));
            cancellationToken.ThrowIfCancellationRequested();
            if (format == ExportFormat.Mp3)
                EncodeMp3(tempWav, tempMp3, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(format == ExportFormat.Mp3 ? tempMp3 : tempWav, destination, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            TryDelete(tempWav);
            TryDelete(tempMp3);
        }
    }

    private static void EncodeMp3(string source, string destination, CancellationToken cancellationToken)
    {
#if PORTABLE
        var psi = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-y", "-i", source,
                     "-codec:a", "libmp3lame", "-b:a", "192k", destination })
            psi.ArgumentList.Add(arg);
        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("FFmpeg did not start."); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("FFmpeg is required for MP3 export on macOS and Linux. Install ffmpeg and reopen RMS, or export WAV.", ex);
        }
        using (process)
        {
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                while (!process.WaitForExit(100))
                    cancellationToken.ThrowIfCancellationRequested();
                cancellationToken.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("MP3 export failed: " + error.GetAwaiter().GetResult());
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
                error.GetAwaiter().GetResult();
            }
        }
#else
        MediaFoundationApi.Startup();
        using var reader = new MediaFoundationReader(source);
        // Media Foundation finishes its current encoding call before cancellation is observed.
        MediaFoundationEncoder.EncodeToMp3(reader, destination, 192000);
        cancellationToken.ThrowIfCancellationRequested();
#endif
    }

    private static ProjectDocument FilterForScope(ProjectDocument project, ExportScope scope)
    {
        var cloneJson = Core.Editing.ProjectSerializer.ToJson(project);
        var clone = Core.Editing.ProjectSerializer.FromJson(cloneJson, project.RootPath);
        var keep = new HashSet<string>(clone.ExportableTracks(scope).Select(t => t.Id));
        clone.Tracks.RemoveAll(t => !keep.Contains(t.Id));
        return clone;
    }

    private static void RenderWav(string path, ProjectMixer mixer, long frames, int sampleRate, int bits,
        CancellationToken cancellationToken, Action<double> report)
    {
        var format = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, sampleRate, 2,
            sampleRate * 2 * (bits / 8), 2 * (bits / 8), bits);
        // Standard RIFF WAV uses a 32-bit size. Fail before writing a truncated header.
        if (frames > (uint.MaxValue - 100L) / format.BlockAlign)
            throw new InvalidOperationException("This export is too long for a WAV file. Export shorter marked ranges.");
        using var writer = new WaveFileWriter(path, format);
        var chunk = new float[8192];
        long rendered = 0;
        var lastPercent = -1;
        while (rendered < frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(chunk.Length / 2, frames - rendered) * 2;
            var read = mixer.Read(chunk, 0, count);
            if (read != count) throw new IOException("The song stopped rendering before the export was complete.");
            for (var i = 0; i < read; i++)
            {
                var sample = Math.Clamp(chunk[i], -1f, 1f);
                if (bits == 16)
                    writer.WriteSample(sample);
                else
                {
                    var value = (int)Math.Round(sample * 8388607.0);
                    writer.WriteByte((byte)(value & 0xFF));
                    writer.WriteByte((byte)((value >> 8) & 0xFF));
                    writer.WriteByte((byte)((value >> 16) & 0xFF));
                }
            }
            rendered += read / 2;
            var percent = (int)(rendered * 100.0 / frames);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                report(rendered / (double)frames);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* a leftover temporary export can be removed later */ }
        catch (UnauthorizedAccessException) { /* preserve the original error */ }
    }
}
