using System.Diagnostics;
using System.Runtime.InteropServices;
using RyanMusicStudio.Core.Model;
using RyanMusicStudio.Core.Persistence;
using RyanMusicStudio.Engine.IO;

namespace RyanMusicStudio.Engine.Media;

public sealed class MediaImporter
{
    public AudioMedia Import(ProjectDocument project, string sourcePath, Track? targetTrack)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Audio file not found.", sourcePath);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not ".wav" and not ".mp3")
            throw new InvalidOperationException("Import a WAV or MP3 file.");

        var paths = new ProjectPaths(project.RootPath);
        paths.EnsureLayout();
        var media = new AudioMedia { OriginalFileName = Path.GetFileName(sourcePath) };
        media.OriginalRelativePath = $"media/originals/{media.Id}{ext}";
        media.WorkingRelativePath = $"media/working/{media.Id}.wav";
        var original = Path.Combine(paths.Root, media.OriginalRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var working = Path.Combine(paths.Root, media.WorkingRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Copy(sourcePath, original, true);

        // FFmpeg is invoked only for file conversion. Live capture and playback
        // use PortAudio; no external process runs in a real-time callback.
        var psi = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-i", sourcePath,
                 "-f", "f32le", "-acodec", "pcm_f32le", "-ar", project.SampleRate.ToString(),
                 "-ac", "2", "pipe:1" }) psi.ArgumentList.Add(arg);

        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("FFmpeg did not start."); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("FFmpeg is required to import WAV and MP3 on macOS and Linux. Install ffmpeg and reopen RMS.", ex);
        }

        using (process)
        {
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                using var writer = new IncrementalWavWriter(working, project.SampleRate, 2);
                var bytes = new byte[65536 + 8];
                var carry = 0;
                long frames = 0;
                int read;
                while ((read = process.StandardOutput.BaseStream.Read(bytes, carry, 65536)) > 0)
                {
                    var available = carry + read;
                    var complete = available - available % 8; // stereo float frames
                    writer.WriteInterleavedFloat(MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, complete)));
                    frames += complete / 8;
                    carry = available - complete;
                    if (carry != 0) Array.Copy(bytes, complete, bytes, 0, carry);
                }
                writer.FinalizeHeader();
                process.WaitForExit();
                if (process.ExitCode != 0 || carry != 0)
                    throw new InvalidDataException("Could not decode audio: " + stderr.GetAwaiter().GetResult());
                media.SourceSampleRate = project.SampleRate;
                media.WorkingSampleRate = project.SampleRate;
                media.Channels = 2;
                media.BitDepth = 32;
                media.LengthFrames = frames;
            }
            catch
            {
                if (!process.HasExited) process.Kill(true);
                try { File.Delete(working); } catch { }
                throw;
            }
        }

        project.Media.Add(media);
        targetTrack ??= project.Tracks.FirstOrDefault(t => t.Role == TrackRole.Backing)
                        ?? project.Tracks.FirstOrDefault();
        if (targetTrack != null)
        {
            targetTrack.Clips.Add(new AudioClip
            {
                MediaId = media.Id, StartFrame = 0, SourceOffsetFrames = 0,
                LengthFrames = media.LengthFrames,
                FadeOutFrames = Math.Min(project.SampleRate / 50, media.LengthFrames / 20)
            });
            if (targetTrack.Role == TrackRole.Backing)
                targetTrack.Channels = TrackChannelLayout.Stereo;
        }
        project.Touch();
        return media;
    }
}
