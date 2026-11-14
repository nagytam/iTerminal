using System.Diagnostics;
using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Video;

/// <summary>
/// Every bitmap format the picture viewer sends to ffmpeg is written here by ffmpeg, then decoded through
/// <see cref="FfmpegVideoDecoder"/>. A format this ffmpeg cannot write is inconclusive, not failed: the decode is then
/// unchecked on this machine.
/// </summary>
[TestClass]
public class FfmpegBitmapFormatsTest
{
    public static IEnumerable<object[]> Extensions() =>
        ImageFileTypes.FfmpegBitmapExtensions.Select(extension => new object[] { extension });

    [Timeout(60_000)]
    [TestMethod]
    [DynamicData(nameof(Extensions))]
    public void EachFfmpegBitmap_DecodesThroughFfmpeg(string extension)
    {
        var factory = new FfmpegVideoDecoderFactory();
        if (!factory.IsAvailable)
        {
            Assert.Inconclusive("needs ffmpeg and ffprobe on PATH");
        }

        string folder = Path.Combine(Path.GetTempPath(), "ffmpeg-bitmap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "picture" + extension);
            if (!WriteSample(path))
            {
                Assert.Inconclusive($"this ffmpeg cannot write {extension}");
            }

            using IVideoDecoder decoder = factory.Create(path);
            decoder.Open(TimeSpan.Zero, 16, 12);
            byte[] rgb = new byte[16 * 12 * 3];
            Assert.IsTrue(decoder.ReadFrame(rgb), $"ffmpeg decodes {extension}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>One frame of ffmpeg's test pattern, written to <paramref name="path"/>; false when ffmpeg refuses.</summary>
    private static bool WriteSample(string path)
    {
        var startInfo = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=64x48", "-frames:v", "1", path })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null || !process.WaitForExit(30_000))
        {
            return false;
        }

        return process.ExitCode == 0 && File.Exists(path);
    }
}
