using iTerminal.Core.Audio;
using iTerminal.Core.Audio.Decoders;
using System.Buffers.Binary;

namespace iTerminal.Core.Test.Audio;

[TestClass]
public class WaveAudioDecoderTest
{
    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "WaveDecoderTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_Pcm16_PassesSamplesThroughUnchanged()
    {
        var samples = TestAudioFiles.Pcm16(0, 1000, -1000, short.MaxValue, short.MinValue, 42);
        var path = TestAudioFiles.WriteWave(_tempRoot, "pcm16.wav", samples, sampleRate: 8000, channels: 2, bitsPerSample: 16);

        using var decoder = new WaveAudioDecoder(path);

        Assert.AreEqual(8000, decoder.Format.SampleRate);
        Assert.AreEqual(2, decoder.Format.Channels);

        var buffer = new byte[samples.Length];
        var read = decoder.Read(buffer);

        Assert.AreEqual(samples.Length, read);
        CollectionAssert.AreEqual(samples, buffer);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_Pcm8_CentresAndScalesToSixteenBit()
    {
        // 8-bit WAV is unsigned with a 128 bias: 128 is silence, 255 is near full scale.
        var path = TestAudioFiles.WriteWave(
            _tempRoot, "pcm8.wav", [128, 255, 0], sampleRate: 8000, channels: 1, bitsPerSample: 8);

        using var decoder = new WaveAudioDecoder(path);
        var buffer = new byte[6];
        var read = decoder.Read(buffer);

        Assert.AreEqual(6, read);
        Assert.AreEqual(0, BinaryPrimitives.ReadInt16LittleEndian(buffer));
        Assert.AreEqual((short)(127 << 8), BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(2)));
        Assert.AreEqual((short)(-128 << 8), BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(4)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_IeeeFloat32_ClampsAndScalesToSixteenBit()
    {
        var floats = new[] { 0f, 0.5f, -0.5f, 2f, -2f };
        var data = new byte[floats.Length * 4];
        for (int i = 0; i < floats.Length; i++)
        {
            BitConverter.GetBytes(floats[i]).CopyTo(data, i * 4);
        }

        var path = TestAudioFiles.WriteWave(
            _tempRoot, "float32.wav", data, sampleRate: 8000, channels: 1, bitsPerSample: 32,
            formatTag: TestAudioFiles.FormatIeeeFloat);

        using var decoder = new WaveAudioDecoder(path);
        var buffer = new byte[floats.Length * 2];
        decoder.Read(buffer);

        Assert.AreEqual(0, BinaryPrimitives.ReadInt16LittleEndian(buffer));
        Assert.AreEqual(16383, BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(2)));
        Assert.AreEqual(-16383, BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(4)));
        Assert.AreEqual(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(6)), "Values above 1.0 must clamp");
        Assert.AreEqual(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(8)), "Values below -1.0 must clamp");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Duration_MatchesSampleCount()
    {
        var path = TestAudioFiles.WriteSilence(_tempRoot, "silence.wav", TimeSpan.FromSeconds(2), sampleRate: 8000);

        using var decoder = new WaveAudioDecoder(path);

        Assert.AreEqual(2.0, decoder.Duration.TotalSeconds, 0.01);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_AtEndOfStream_ReturnsZero()
    {
        var path = TestAudioFiles.WriteWave(_tempRoot, "short.wav", TestAudioFiles.Pcm16(1, 2), channels: 1);

        using var decoder = new WaveAudioDecoder(path);
        var buffer = new byte[1024];

        Assert.AreEqual(4, decoder.Read(buffer));
        Assert.AreEqual(0, decoder.Read(buffer), "A second read past the data chunk is end-of-stream");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TrySeek_RepositionsToRequestedTime()
    {
        // One second of mono 16-bit at 8 kHz, each sample numbered by its frame index.
        var samples = new short[8000];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(i % short.MaxValue);
        }

        var path = TestAudioFiles.WriteWave(
            _tempRoot, "ramp.wav", TestAudioFiles.Pcm16(samples), sampleRate: 8000, channels: 1);

        using var decoder = new WaveAudioDecoder(path);

        Assert.IsTrue(decoder.TrySeek(TimeSpan.FromSeconds(0.5)));

        var buffer = new byte[2];
        decoder.Read(buffer);

        Assert.AreEqual(4000, BinaryPrimitives.ReadInt16LittleEndian(buffer));
        Assert.AreEqual(0.5, decoder.Position.TotalSeconds, 0.01);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_OnNonRiffFile_Throws()
    {
        var path = Path.Combine(_tempRoot, "notaudio.wav");
        File.WriteAllText(path, "this is definitely not a wave file");

        Assert.ThrowsExactly<InvalidDataException>(() => new WaveAudioDecoder(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanDecode_DistinguishesSupportedFromUnsupportedContainers()
    {
        var supported = TestAudioFiles.WriteWave(_tempRoot, "ok.wav", TestAudioFiles.Pcm16(1, 2, 3, 4));
        var garbage = Path.Combine(_tempRoot, "bad.wav");
        File.WriteAllText(garbage, "nope");

        Assert.IsTrue(WaveAudioDecoder.CanDecode(supported));
        Assert.IsFalse(WaveAudioDecoder.CanDecode(garbage));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanDecode_OnCompressedWaveContainer_IsFalseSoFactoryCanFallBack()
    {
        // Format tag 0x0011 is IMA ADPCM: a legitimate .wav this decoder deliberately rejects.
        var path = TestAudioFiles.WriteWave(
            _tempRoot, "adpcm.wav", new byte[64], sampleRate: 8000, channels: 1, bitsPerSample: 4, formatTag: 0x0011);

        Assert.IsFalse(WaveAudioDecoder.CanDecode(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_HonoursBlockAlignmentWhenBufferIsNotAMultipleOfAFrame()
    {
        var path = TestAudioFiles.WriteWave(
            _tempRoot, "stereo.wav", TestAudioFiles.Pcm16(1, 2, 3, 4, 5, 6), sampleRate: 8000, channels: 2);

        using var decoder = new WaveAudioDecoder(path);

        // 7 bytes is less than two whole stereo frames; only complete frames may be returned.
        var read = decoder.Read(new byte[7]);

        Assert.AreEqual(4, read);
        Assert.AreEqual(0, read % new PcmFormat(8000, 2).BlockAlign);
    }
}
