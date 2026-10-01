using RyanMusicStudio.Engine.IO;
using Xunit;

namespace RyanMusicStudio.Tests;

public class ResamplerTests
{
    // WASAPI delivers 10 ms packets; a per-block resampler used to drop a frame per packet
    // (48 kHz mic into a 44.1 kHz song), so takes drifted ahead of the backing track.
    [Theory]
    [InlineData(48000, 44100, 480)]
    [InlineData(44100, 48000, 441)]
    [InlineData(16000, 48000, 160)]
    [InlineData(48000, 44100, 2048)]
    public void Ten_seconds_of_blocks_keep_the_right_length(int inRate, int outRate, int block)
    {
        var resampler = new StreamResampler();
        var ratio = inRate / (double)outRate;
        var src = new float[block];
        var dst = new float[(int)(block / ratio) + 2];
        long total = 0;
        var blocks = inRate * 10 / block;
        for (var b = 0; b < blocks; b++)
            total += resampler.Process(src, ratio, dst);

        // The resampler holds back one input sample (up to 1/ratio outputs) for the next block;
        // anything beyond that would be drift.
        var expected = (long)(blocks * (double)block / ratio);
        var slack = (long)Math.Ceiling(1 / ratio) + 1;
        Assert.InRange(total, expected - slack, expected + slack);
    }

    [Fact]
    public void Block_edges_do_not_jump()
    {
        // A slow ramp resampled block by block must stay a smooth ramp across block boundaries.
        var resampler = new StreamResampler();
        const double ratio = 48000 / 44100.0;
        var output = new List<float>();
        var dst = new float[600];
        for (var b = 0; b < 20; b++)
        {
            var src = Enumerable.Range(b * 480, 480).Select(i => i / 10000f).ToArray();
            var n = resampler.Process(src, ratio, dst);
            output.AddRange(dst.Take(n));
        }

        var step = (float)(ratio / 10000);
        for (var i = 1; i < output.Count; i++)
            Assert.InRange(output[i] - output[i - 1], step * 0.99f, step * 1.01f);
    }
}
