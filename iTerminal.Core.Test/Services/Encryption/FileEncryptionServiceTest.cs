using System.Buffers.Binary;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Encryption;

namespace iTerminal.Core.Test.Services.Encryption;

[TestClass]
public class FileEncryptionServiceTest
{
    private const string Passphrase = "correct horse battery staple";

    // A small chunk and a low iteration count keep the tests fast; the format is the same.
    private readonly FileEncryptionService _service = new(new FileSystemService()) { Iterations = FileEncryptionService.MinIterations, ChunkSize = 1000 };
    private string _folder = null!;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iTerminalEnc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static byte[] Data(int length)
    {
        var data = new byte[length];
        new Random(length).NextBytes(data);
        return data;
    }

    private async Task<byte[]> EncryptAsync(byte[] plain, string passphrase = Passphrase)
    {
        using var output = new MemoryStream();
        await _service.EncryptAsync(new MemoryStream(plain), output, passphrase);
        return output.ToArray();
    }

    private async Task<byte[]> DecryptAsync(byte[] sealedData, string passphrase = Passphrase)
    {
        using var output = new MemoryStream();
        await _service.DecryptAsync(new MemoryStream(sealedData), output, passphrase);
        return output.ToArray();
    }

    private async Task<EncryptionException> DecryptFailsAsync(byte[] sealedData, string passphrase = Passphrase)
    {
        try
        {
            await DecryptAsync(sealedData, passphrase);
        }
        catch (EncryptionException exception)
        {
            return exception;
        }

        Assert.Fail("The decryption should have been refused.");
        return null!;
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(999)]
    [DataRow(1000)]
    [DataRow(1001)]
    [DataRow(2000)]
    [DataRow(4567)]
    public async Task RoundTrip_ReturnsTheSameBytes(int length)
    {
        var plain = Data(length);

        var sealedData = await EncryptAsync(plain);
        var back = await DecryptAsync(sealedData);

        CollectionAssert.AreEqual(plain, back);
        var chunks = Math.Max(1, (length + 999) / 1000);
        Assert.AreEqual(FileEncryptionService.HeaderLength + length + chunks * FileEncryptionService.TagLength, sealedData.Length);
    }

    [TestMethod]
    public async Task Ciphertext_DoesNotContainThePlaintext_AndDiffersEveryTime()
    {
        var plain = System.Text.Encoding.ASCII.GetBytes(new string('A', 500) + "SECRET-MARKER" + new string('B', 500));

        var first = await EncryptAsync(plain);
        var second = await EncryptAsync(plain);

        Assert.IsFalse(first.AsSpan().IndexOf("SECRET-MARKER"u8) >= 0);
        CollectionAssert.AreNotEqual(first, second, "a fresh salt and nonce per file");
        CollectionAssert.AreNotEqual(first[16..32], second[16..32], "the salt differs");
    }

    [TestMethod]
    public async Task Header_StatesTheFormat()
    {
        var sealedData = await EncryptAsync(Data(10));

        CollectionAssert.AreEqual("ITENC"u8.ToArray(), sealedData[..5]);
        Assert.AreEqual(1, sealedData[5], "version");
        Assert.AreEqual((uint)FileEncryptionService.MinIterations, BinaryPrimitives.ReadUInt32LittleEndian(sealedData.AsSpan(8)));
        Assert.AreEqual(1000u, BinaryPrimitives.ReadUInt32LittleEndian(sealedData.AsSpan(12)));
    }

    [TestMethod]
    public async Task UnicodePassphrase_RoundTrips_InEitherNormalisation()
    {
        var plain = Data(50);
        var sealedData = await EncryptAsync(plain, "caf\u00e9 \u00fc\u00f1\u00ed");

        CollectionAssert.AreEqual(plain, await DecryptAsync(sealedData, "cafe\u0301 u\u0308n\u0303i\u0301"));
    }

    [TestMethod]
    public async Task LoneSurrogatePassphrase_StillRoundTrips()
    {
        var plain = Data(30);
        var passphrase = "abc" + (char)0xD800 + "def";
        var sealedData = await EncryptAsync(plain, passphrase);

        CollectionAssert.AreEqual(plain, await DecryptAsync(sealedData, passphrase));
    }

    [TestMethod]
    public async Task WrongPassphrase_IsRefused_WithAClearReason()
    {
        var sealedData = await EncryptAsync(Data(2500));

        var failure = await DecryptFailsAsync(sealedData, "not the passphrase");

        Assert.AreEqual(EncryptionFailure.WrongPassphraseOrCorrupt, failure.Failure);
        StringAssert.Contains(failure.Message, "Wrong passphrase");
    }

    [TestMethod]
    public async Task TamperedChunk_FailsAuthentication_AtThatChunk()
    {
        var sealedData = await EncryptAsync(Data(3500));
        // Second chunk: flip one ciphertext bit.
        sealedData[FileEncryptionService.HeaderLength + 1016 + 10] ^= 0x01;

        var failure = await DecryptFailsAsync(sealedData);

        Assert.AreEqual(EncryptionFailure.Corrupt, failure.Failure);
        StringAssert.Contains(failure.Message, "part 2");
    }

    [TestMethod]
    public async Task TamperedTag_AndTamperedHeader_AreRefused()
    {
        var plain = Data(1500);
        var tag = await EncryptAsync(plain);
        tag[^1] ^= 0xFF;
        await DecryptFailsAsync(tag);

        // The salt is part of the header, which every chunk authenticates; the key also changes, so either way it fails.
        var salt = await EncryptAsync(plain);
        salt[20] ^= 0x01;
        await DecryptFailsAsync(salt);

        // The nonce prefix is read from the header and bound into the chunk's nonce.
        var nonce = await EncryptAsync(plain);
        nonce[35] ^= 0x01;
        await DecryptFailsAsync(nonce);
    }

    [TestMethod]
    public async Task ChunksSwapped_AreRefused()
    {
        var sealedData = await EncryptAsync(Data(3000));
        var swapped = sealedData.ToArray();
        Array.Copy(sealedData, FileEncryptionService.HeaderLength + 1016, swapped, FileEncryptionService.HeaderLength, 1016);
        Array.Copy(sealedData, FileEncryptionService.HeaderLength, swapped, FileEncryptionService.HeaderLength + 1016, 1016);

        await DecryptFailsAsync(swapped);
    }

    [TestMethod]
    public async Task Truncation_AtAChunkBoundary_IsRefused()
    {
        var sealedData = await EncryptAsync(Data(3500));

        // Whole chunks removed from the end: every remaining chunk authenticates, but none was sealed as the last.
        var failure = await DecryptFailsAsync(sealedData[..(FileEncryptionService.HeaderLength + 2 * 1016)]);

        Assert.AreEqual(EncryptionFailure.Corrupt, failure.Failure);
    }

    [TestMethod]
    public async Task Truncation_InsideAChunk_AndOfTheTagOnly_AreRefused()
    {
        var sealedData = await EncryptAsync(Data(2500));

        await DecryptFailsAsync(sealedData[..^1]);
        await DecryptFailsAsync(sealedData[..^20]);
        await DecryptFailsAsync(sealedData[..(FileEncryptionService.HeaderLength + 5)]);
        await DecryptFailsAsync(sealedData[..FileEncryptionService.HeaderLength]);
    }

    [TestMethod]
    public async Task DataAppendedAfterTheEnd_IsRefused()
    {
        var sealedData = await EncryptAsync(Data(1500));

        await DecryptFailsAsync([.. sealedData, .. Data(40)]);
        await DecryptFailsAsync([.. sealedData, 0]);
    }

    [TestMethod]
    public async Task HeaderTruncated_AndNotAnEncryptedFile_AreToldApart()
    {
        var sealedData = await EncryptAsync(Data(10));

        Assert.AreEqual(EncryptionFailure.BadHeader, (await DecryptFailsAsync(sealedData[..20])).Failure);
        Assert.AreEqual(EncryptionFailure.NotEncrypted, (await DecryptFailsAsync(System.Text.Encoding.ASCII.GetBytes("just a text file, nothing secret"))).Failure);
        Assert.AreEqual(EncryptionFailure.NotEncrypted, (await DecryptFailsAsync([])).Failure);
    }

    [TestMethod]
    public async Task UnknownVersion_AndOutOfRangeValues_AreRefusedBeforeTheKeyIsDerived()
    {
        var sealedData = await EncryptAsync(Data(10));

        var version = sealedData.ToArray();
        version[5] = 2;
        Assert.AreEqual(EncryptionFailure.Unsupported, (await DecryptFailsAsync(version)).Failure);

        var iterations = sealedData.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(iterations.AsSpan(8), uint.MaxValue);
        Assert.AreEqual(EncryptionFailure.BadHeader, (await DecryptFailsAsync(iterations)).Failure);

        var low = sealedData.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(low.AsSpan(8), 10);
        Assert.AreEqual(EncryptionFailure.BadHeader, (await DecryptFailsAsync(low)).Failure);

        var chunk = sealedData.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), uint.MaxValue);
        Assert.AreEqual(EncryptionFailure.BadHeader, (await DecryptFailsAsync(chunk)).Failure);
    }

    [TestMethod]
    public async Task EmptyPassphrase_IsRefused()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => EncryptAsync(Data(5), string.Empty));
    }

    [TestMethod]
    public async Task Cancellation_StopsTheStream()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.EncryptAsync(new MemoryStream(Data(5000)), new MemoryStream(), Passphrase, null, cancellation.Token));
    }

    [TestMethod]
    public void TargetNames_FollowTheMode()
    {
        Assert.AreEqual(@"C:\a\report.pdf.enc", _service.TargetPathFor(@"C:\a\report.pdf", EncryptionMode.Encrypt));
        Assert.AreEqual(@"C:\a\report.pdf", _service.TargetPathFor(@"C:\a\report.pdf.enc", EncryptionMode.Decrypt));
        Assert.AreEqual(@"C:\a\report.pdf", _service.TargetPathFor(@"C:\a\report.pdf.ENC", EncryptionMode.Decrypt));
        Assert.AreEqual(@"C:\a\notes.txt.decrypted", _service.TargetPathFor(@"C:\a\notes.txt", EncryptionMode.Decrypt));
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string[] Leftovers() => Directory.GetFiles(_folder, ".*.tmp");

    [TestMethod]
    public async Task Process_EncryptsThenDecrypts_FilesSeparately_AndLeavesTheOriginals()
    {
        var a = Write("a.bin", Data(2500));
        var b = Write("b.txt", Data(10));
        var original = File.ReadAllBytes(a);

        var encrypted = await _service.ProcessAsync([a, b], EncryptionMode.Encrypt, Passphrase);

        Assert.AreEqual(2, encrypted.Succeeded);
        Assert.IsTrue(File.Exists(a + ".enc") && File.Exists(b + ".enc"));
        Assert.IsTrue(File.Exists(a), "the original is not touched");
        Assert.AreEqual(0, Leftovers().Length);

        File.Delete(a);
        var decrypted = await _service.ProcessAsync([a + ".enc"], EncryptionMode.Decrypt, Passphrase);

        Assert.AreEqual(1, decrypted.Succeeded);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(a));
    }

    [TestMethod]
    public async Task Process_NeverOverwrites_AnExistingTarget_ButStillRunsTheOthers()
    {
        var a = Write("a.bin", Data(100));
        var b = Write("b.bin", Data(100));
        var existing = Write("a.bin.enc", [1, 2, 3]);

        var result = await _service.ProcessAsync([a, b], EncryptionMode.Encrypt, Passphrase);

        Assert.AreEqual(1, result.Failed);
        StringAssert.Contains(result.Items[0].Error!, "already exists");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(existing));
        Assert.IsTrue(File.Exists(b + ".enc"));
        Assert.AreEqual(0, Leftovers().Length);
    }

    [TestMethod]
    public async Task Process_DecryptingANonEncName_WritesDotDecrypted()
    {
        var a = Write("a.bin", Data(100));
        await _service.ProcessAsync([a], EncryptionMode.Encrypt, Passphrase);
        var renamed = Path.Combine(_folder, "renamed.dat");
        File.Move(a + ".enc", renamed);

        var result = await _service.ProcessAsync([renamed], EncryptionMode.Decrypt, Passphrase);

        Assert.AreEqual(1, result.Succeeded);
        Assert.IsTrue(File.Exists(renamed + ".decrypted"));
    }

    [TestMethod]
    public async Task Process_WrongPassphrase_LeavesEverythingUntouched()
    {
        var a = Write("a.bin", Data(2500));
        await _service.ProcessAsync([a], EncryptionMode.Encrypt, Passphrase);
        File.Delete(a);
        var before = File.ReadAllBytes(a + ".enc");

        var result = await _service.ProcessAsync([a + ".enc"], EncryptionMode.Decrypt, "wrong");

        Assert.AreEqual(1, result.Failed);
        StringAssert.Contains(result.Items[0].Error!, "Wrong passphrase");
        Assert.IsFalse(File.Exists(a), "no plaintext appears");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(a + ".enc"));
        Assert.AreEqual(0, Leftovers().Length, "no half-written file is left");
    }

    [TestMethod]
    public async Task Process_TamperedFile_FailsInTheMiddle_AndLeavesNoPartialOutput()
    {
        var a = Write("a.bin", Data(5000));
        await _service.ProcessAsync([a], EncryptionMode.Encrypt, Passphrase);
        File.Delete(a);
        var bytes = File.ReadAllBytes(a + ".enc");
        bytes[FileEncryptionService.HeaderLength + 3 * 1016 + 5] ^= 0x10;
        File.WriteAllBytes(a + ".enc", bytes);

        var result = await _service.ProcessAsync([a + ".enc"], EncryptionMode.Decrypt, Passphrase);

        Assert.AreEqual(1, result.Failed);
        StringAssert.Contains(result.Items[0].Error!, "part 4");
        Assert.IsFalse(File.Exists(a));
        Assert.AreEqual(0, Leftovers().Length);
    }

    [TestMethod]
    public async Task Process_FolderOrMissingFile_IsListedAsFailed()
    {
        var folder = Path.Combine(_folder, "sub");
        Directory.CreateDirectory(folder);

        var result = await _service.ProcessAsync([folder, Path.Combine(_folder, "missing.txt")], EncryptionMode.Encrypt, Passphrase);

        Assert.AreEqual(2, result.Failed);
    }

    [TestMethod]
    public async Task Process_Cancelled_LeavesNoTempFile_AndWritesNothing()
    {
        var a = Write("a.bin", Data(100));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await _service.ProcessAsync([a], EncryptionMode.Encrypt, Passphrase, null, cancellation.Token);

        Assert.IsTrue(result.Cancelled);
        Assert.IsFalse(File.Exists(a + ".enc"));
        Assert.AreEqual(0, Leftovers().Length);
    }

    [TestMethod]
    public async Task Process_ReportsProgress_UpToTheTotal()
    {
        var a = Write("a.bin", Data(4000));
        var reports = new List<EncryptionProgress>();

        await _service.ProcessAsync([a], EncryptionMode.Encrypt, Passphrase, new SyncProgress(reports.Add));

        Assert.IsTrue(reports.Count > 0);
        Assert.AreEqual(4000, reports[^1].BytesTotal);
        Assert.AreEqual(4000, reports[^1].BytesDone);
    }

    private sealed class SyncProgress(Action<EncryptionProgress> report) : IProgress<EncryptionProgress>
    {
        public void Report(EncryptionProgress value) => report(value);
    }

    [TestMethod]
    public void DefaultSettings_AreTheDocumentedOnes()
    {
        var defaults = new FileEncryptionService(new FileSystemService());

        Assert.AreEqual(600_000, defaults.Iterations);
        Assert.AreEqual(1024 * 1024, defaults.ChunkSize);
    }
}
