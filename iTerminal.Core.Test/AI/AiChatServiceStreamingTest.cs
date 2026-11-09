using iTerminal.Core.AI;
using iConsole.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Runtime.CompilerServices;

namespace iTerminal.Core.Test.AI;

[TestClass]
public class AiChatServiceStreamingTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task SendMessage_PublishesPartialTextBeforeTheResponseCompletes_AndKeepsToolsEnabled()
    {
        var firstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StreamingChatClient client = new(GetUpdates);
        AiChatService service = CreateService(client);
        AiChatModel model = new() { Input = "question" };

        Task send = service.SendMessageAsync(model);
        await firstUpdate.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("Hello", model.LastResponse);
        Assert.IsTrue(model.IsThinking);
        Assert.IsNotNull(client.Options?.Tools);
        Assert.IsTrue(client.Options.Tools.Count > 0);

        finishResponse.SetResult();
        await send.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("Hello world", model.LastResponse);
        Assert.AreEqual("Hello world", model.History[^1].Content);
        Assert.IsFalse(model.IsThinking);

        async IAsyncEnumerable<ChatResponseUpdate> GetUpdates([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Hello");
            firstUpdate.SetResult();
            await finishResponse.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, " world");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SendMessage_DoesNotPublishPartialThinkingOrAnsiSequences()
    {
        var thinkingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishThinking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var privateAnsiStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishPrivateAnsi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StreamingChatClient client = new(GetUpdates);
        AiChatService service = CreateService(client);
        AiChatModel model = new() { Input = "question" };

        Task send = service.SendMessageAsync(model);
        await thinkingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("Visible", model.LastResponse);

        finishThinking.SetResult();
        await privateAnsiStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("Visible answer!", model.LastResponse);

        finishPrivateAnsi.SetResult();
        await send.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("Visible answer!", model.LastResponse);

        async IAsyncEnumerable<ChatResponseUpdate> GetUpdates([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Visible");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "<thi");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "nk>private reasoning");
            thinkingStarted.SetResult();
            await finishThinking.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "</think> answer\x1b[31m!");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "\x1b[?25");
            privateAnsiStarted.SetResult();
            await finishPrivateAnsi.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "h");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SendMessage_CancellationClearsThinkingAndReportsCancellation()
    {
        var firstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StreamingChatClient client = new(GetUpdates);
        AiChatService service = CreateService(client);
        AiChatModel model = new() { Input = "question" };
        using CancellationTokenSource cancellation = new();

        Task send = service.SendMessageAsync(model, cancellation.Token);
        await firstUpdate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await send.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("Cancelled.", model.LastResponse);
        Assert.IsFalse(model.IsThinking);
        Assert.AreEqual(1, model.History.Count);

        async IAsyncEnumerable<ChatResponseUpdate> GetUpdates([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            firstUpdate.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SendMessage_ReportsStreamingErrorsAndClearsThinking()
    {
        StreamingChatClient client = new(GetUpdates);
        AiChatService service = CreateService(client);
        AiChatModel model = new() { Input = "question" };

        await service.SendMessageAsync(model);

        StringAssert.StartsWith(model.LastResponse, "Error: stream failed");
        Assert.IsFalse(model.IsThinking);

        async IAsyncEnumerable<ChatResponseUpdate> GetUpdates([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            await Task.Yield();
            throw new InvalidOperationException("stream failed");
        }
    }

    private static AiChatService CreateService(IChatClient client) =>
        new(client, Mock.Of<IContentModelService>(), Mock.Of<IUiService>(), Options.Create(new AiOptions()));

    private sealed class StreamingChatClient(
        Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> getUpdates) : IChatClient
    {
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            return getUpdates(cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
