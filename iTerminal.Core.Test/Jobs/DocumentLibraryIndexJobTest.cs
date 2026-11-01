using iTerminal.Core.Jobs;
using iTerminal.Core.Services.Documents;
using Moq;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class DocumentLibraryIndexJobTest
{
    [TestMethod]
    [Timeout(10000)]
    public async Task Job_ScansOnlyAfterIndexingIsRequested()
    {
        TaskCompletionSource requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IDocumentLibraryService> library = new();
        library.SetupGet(service => service.IndexingRequested).Returns(requested.Task);
        library.SetupGet(service => service.DocumentsFolder).Returns(Path.GetTempPath());
        library.Setup(service => service.RefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        DocumentLibraryIndexJob job = new(library.Object) { FolderPollInterval = TimeSpan.FromMilliseconds(20) };
        using CancellationTokenSource cancellation = new();

        Task execution = job.ExecuteAsync(cancellation.Token);
        await Task.Delay(200);

        library.Verify(service => service.Preload(), Times.Once, "The cached index should be read at once.");
        library.Verify(service => service.RefreshAsync(It.IsAny<CancellationToken>()), Times.Never,
            "The folder was scanned before the document manager asked for it.");

        requested.SetResult();
        await WaitForScanAsync(library);

        cancellation.Cancel();
        await execution;
    }

    private static async Task WaitForScanAsync(Mock<IDocumentLibraryService> library)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!library.Invocations.Any(invocation => invocation.Method.Name == nameof(IDocumentLibraryService.RefreshAsync)))
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The scan did not start after it was requested.");
            await Task.Delay(10);
        }
    }
}
