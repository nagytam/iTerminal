using iTerminal.Core.AI;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services.BackgroundStart;
using iTerminal.Core.Services.RuntimeDependencies;
using iTerminal.Core.Test.Services.BackgroundStart;
using Moq;
using System.Net;
using System.Text;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class OllamaServerJobTest
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static OllamaServerJob CreateJob(
        OllamaServer server,
        IEnumerable<IRuntimeDependency> runtimeDependencies,
        IAiChatService chatService,
        BackgroundStartService backgroundStart) =>
        new(server, runtimeDependencies, chatService, new AiChatModel(), Serilog.Core.Logger.None, backgroundStart);

    [TestMethod]
    [Timeout(5000)]
    public async Task DoesNothing_WhenOllamaIsNotTheProvider()
    {
        var chat = new Mock<IAiChatService>();
        var job = CreateJob(
            new OllamaServer(new Uri("http://localhost:11434/")),
            [],
            chat.Object,
            TestBackgroundStart.Configured(BackgroundStartMode.Immediately));

        await job.ExecuteAsync(CancellationToken.None);

        chat.Verify(c => c.CheckAvailabilityAsync(It.IsAny<AiChatModel>()), Times.Never);
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task ChecksTheAiBar_WhenTheServerBecomesReachable()
    {
        // A server that answers means nothing is started, and the bar is checked once the job sees it.
        var server = new OllamaServer(new Uri("http://localhost:11434/"), new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") })));
        var chat = new Mock<IAiChatService>();
        chat.Setup(c => c.CheckAvailabilityAsync(It.IsAny<AiChatModel>())).Returns(Task.CompletedTask);
        // Nothing is installed in this folder, so the job only watches the server and never starts one.
        var dependency = new OllamaRuntimeDependency(Path.Combine(Path.GetTempPath(), $"iTerminal.Job.{Guid.NewGuid():N}"), "qwen3.5:0.8b", server);
        var job = CreateJob(server, [dependency], chat.Object, TestBackgroundStart.Configured(BackgroundStartMode.Immediately));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await job.ExecuteAsync(cancellation.Token);

        chat.Verify(c => c.CheckAvailabilityAsync(It.IsAny<AiChatModel>()), Times.Once);
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task DoesNotCheckTheServerUntilTheAiBarAsks_WhenOnDemand()
    {
        var server = new OllamaServer(new Uri("http://localhost:11434/"), new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") })));
        var chat = new Mock<IAiChatService>();
        chat.Setup(c => c.CheckAvailabilityAsync(It.IsAny<AiChatModel>())).Returns(Task.CompletedTask);
        var dependency = new OllamaRuntimeDependency(Path.Combine(Path.GetTempPath(), $"iTerminal.Job.{Guid.NewGuid():N}"), "qwen3.5:0.8b", server);
        var job = CreateJob(server, [dependency], chat.Object, TestBackgroundStart.Configured(BackgroundStartMode.OnDemand));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await job.ExecuteAsync(cancellation.Token);

        chat.Verify(c => c.CheckAvailabilityAsync(It.IsAny<AiChatModel>()), Times.Never);
    }
}
