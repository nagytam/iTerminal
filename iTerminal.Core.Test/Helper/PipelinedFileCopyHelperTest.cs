using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Helper;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Helper;

/// <summary>A file streamed from one place to another arrives byte for byte, and a failure on either side surfaces instead of hanging.</summary>
[TestClass]
public class PipelinedFileCopyHelperTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize() =>
        _folder = Path.Combine(Path.GetTempPath(), "PipelinedCopy_" + Guid.NewGuid().ToString("N")[..8]);

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
        }
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ALargeFile_ArrivesByteForByte()
    {
        // Larger than the pipe's pause threshold, so the writer really waits on the reader part of the way.
        Directory.CreateDirectory(_folder);
        var source = Path.Combine(_folder, "in.bin");
        var target = Path.Combine(_folder, "out.bin");
        var content = new byte[3 * 1024 * 1024];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(source, content);
        var fileSystem = new FileSystemService();

        await PipelinedFileCopyHelper.CopyAsync(fileSystem, fileSystem, new Url(source), new Url(target), null, CancellationToken.None);

        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(target));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AFailureReadingTheSource_IsRaised_AndTheCopyDoesNotHang()
    {
        var source = new Mock<IFileSystem>();
        source.Setup(f => f.StreamToAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("read failed"));
        var target = new Mock<IFileSystem>();
        target.Setup(f => f.StreamFromAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (Url _, Stream reader, IProgress<long>? _, CancellationToken token) => await reader.CopyToAsync(Stream.Null, token));

        await Assert.ThrowsAsync<IOException>(() =>
            PipelinedFileCopyHelper.CopyAsync(source.Object, target.Object, new Url("a"), new Url("b"), null, CancellationToken.None));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AFailureWritingTheTarget_IsRaised_AndTheReaderIsStopped()
    {
        var source = new Mock<IFileSystem>();
        source.Setup(f => f.StreamToAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (Url _, Stream writer, IProgress<long>? _, CancellationToken token) =>
            {
                await writer.WriteAsync(new byte[4 * 1024 * 1024], token);
            });
        var target = new Mock<IFileSystem>();
        target.Setup(f => f.StreamFromAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("disk refused"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PipelinedFileCopyHelper.CopyAsync(source.Object, target.Object, new Url("a"), new Url("b"), null, CancellationToken.None));
    }
}
