using System.Buffers.Binary;
using System.Text;
using iTerminal.Core.Services.Evidence;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Evidence;

[TestClass]
public class WavFileTest
{
    [TestMethod]
    public void ThePcmSitsAfterACanonicalHeaderThatDescribesIt()
    {
        byte[] pcm = [1, 2, 3, 4, 5, 6];

        var wav = WavFile.Build(pcm, sampleRate: 16000);

        Assert.AreEqual(44 + pcm.Length, wav.Length);
        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.AreEqual(36 + pcm.Length, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
        Assert.AreEqual("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.AreEqual("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.AreEqual(16, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(16)));
        Assert.AreEqual(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)), "PCM format");
        Assert.AreEqual(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)), "mono");
        Assert.AreEqual(16000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.AreEqual(32000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28)), "byte rate: 16000 samples of 2 bytes");
        Assert.AreEqual(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32)), "block align");
        Assert.AreEqual(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)));
        Assert.AreEqual("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.AreEqual(pcm.Length, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        CollectionAssert.AreEqual(pcm, wav[44..]);
    }

    [TestMethod]
    public async Task ANonPositiveRateIsRefused() =>
        await AsyncAssert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.Run(() => WavFile.Build(new byte[2], sampleRate: 0)));
}
