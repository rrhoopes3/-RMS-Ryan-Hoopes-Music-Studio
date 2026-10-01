using RyanMusicStudio.Engine.Audio;
using RyanMusicStudio.Engine.Devices;
using RyanMusicStudio.Engine.Media;

namespace RyanMusicStudio.Spike;

internal static class Program
{
    public static int Main(string[] args)
    {
        var sampleFlag = Array.IndexOf(args, "--write-sample");
        if (sampleFlag >= 0 && sampleFlag + 1 < args.Length)
        {
            var dest = args[sampleFlag + 1];
            SampleProjectBuilder.Create(dest);
            Console.WriteLine("Wrote CC0 sample project to " + dest);
            return 0;
        }

        Console.WriteLine("RMS spike — WASAPI mic + playback + aligned overdub proof");
        Console.WriteLine("Ryan Music Studio  |  offline  |  no account");
        using var engine = new AudioEngine();
        var inputs = engine.Inputs();
        var outputs = engine.Outputs();
        Console.WriteLine();
        Console.WriteLine("Inputs:");
        foreach (var d in inputs)
            Console.WriteLine($"  [{d.Id}] {d.Name}  {d.DetailLine}");
        Console.WriteLine("Outputs:");
        foreach (var d in outputs)
            Console.WriteLine($"  [{d.Id}] {d.Name}  {d.DetailLine}{(AudioDeviceService.LatencyUnsuitable(d) ? "  ** high latency / wireless warning" : "")}");

        if (args.Contains("--list-only", StringComparer.OrdinalIgnoreCase))
            return inputs.Count == 0 ? 2 : 0;

        if (inputs.Count == 0 || outputs.Count == 0)
        {
            Console.WriteLine("Need at least one mic and one output.");
            return 2;
        }

        engine.Configure(new EngineConfig
        {
            InputDeviceId = inputs[0].Id,
            OutputDeviceId = outputs[0].Id,
            BufferMilliseconds = 20,
            SoftwareMonitor = false
        });

        var tmp = Path.Combine(Path.GetTempPath(), "rms-spike");
        Directory.CreateDirectory(tmp);
        Console.WriteLine();
        Console.WriteLine("Playing headphone test tone (2s)…");
        engine.PlayTestTone();
        Thread.Sleep(2300);
        engine.Stop();

        Console.WriteLine("Recording a 2-second test take…");
        var take = engine.RecordTestTakeAsync(TimeSpan.FromSeconds(2), tmp).GetAwaiter().GetResult();
        Console.WriteLine("Wrote " + take);
        if (!File.Exists(take) || new FileInfo(take).Length < 100)
        {
            Console.WriteLine("FAIL: test take was not committed to disk.");
            return 1;
        }

        Console.WriteLine("Playing the test take back…");
        engine.PlayFile(take);
        Thread.Sleep(2200);
        engine.Stop();

        Console.WriteLine($"Latency compensation frames: {engine.ReportedCompensationFrames}");
        Console.WriteLine("Spike finished. Mic input, output test, disk commit, and playback all ran.");
        return 0;
    }
}
