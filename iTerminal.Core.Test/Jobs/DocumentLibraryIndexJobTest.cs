using iTerminal.Core.Jobs;
using iTerminal.Core.Services.BackgroundStart;
using iTerminal.Core.Services.Documents;
using iTerminal.Core.Test.Services.BackgroundStart;
using Moq;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class DocumentLibraryIndexJobTest
{
    [TestMethod]
    [Timeout(10000)]
    public async Task Job_ScansOnlyAfterIndexingIsRequested()
    {
        Mock<IDocumentLibraryService> library = new();
        library.SetupGet(service => service.DocumentsFolder).Returns(Path.GetTempPath());
        library.Setup(service => service.RefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        BackgroundStartService backgroundStart = TestBackgroundStart.Configured(BackgroundStartMode.OnDemand);
        DocumentLibraryIndexJob job = new(library.Object, backgroundStart) { FolderPollInterval = TimeSpan.FromMilliseconds(20) };
        using CancellationTokenSource cancellation = new();

        Task execution = job.ExecuteAsync(cancellation.Token);
        await Task.Delay(200);

        library.Verify(service => service.Preload(), Times.Once, "The cached index should be read at once.");
        library.Verify(service => service.RefreshAsync(It.IsAny<CancellationToken>()), Times.Never,
            "The folder was scanned before the document manager asked for it.");

        backgroundStart.Request(BackgroundServices.DocumentLibrary);
        await WaitForScanAsync(library);

        cancellation.Cancel();
        await execution;
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task Job_ScansAtOnceWhenConfiguredToStartImmediately()
    {
        Mock<IDocumentLibraryService> library = new();
        library.SetupGet(service => service.DocumentsFolder).Returns(Path.GetTempPath());
        library.Setup(service => service.RefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        DocumentLibraryIndexJob job = new(library.Object, TestBackgroundStart.Configured(BackgroundStartMode.Immediately))
        {
            FolderPollInterval = TimeSpan.FromMilliseconds(20),
        };
        using CancellationTokenSource cancellation = new();

        Task execution = job.ExecuteAsync(cancellation.Token);
        await WaitForScanAsync(library);

        cancellation.Cancel();
        await execution;
    }

    private static async Task WaitForScanAsync(Mock<IDocumentLibraryService> library)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!library.Invocations.Any(invocation => invocation.Method.Name == nameof(IDocumentLibraryService.RefreshAsync)))
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The scan did not start in time.");
            await Task.Delay(10);
        }
    }
}
