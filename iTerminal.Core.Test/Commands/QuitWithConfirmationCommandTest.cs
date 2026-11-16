using iConsole.Prompts;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class QuitWithConfirmationCommandTest
{
    /// <summary>A task that stays queued or running until the test releases it.</summary>
    private sealed class BlockedTask(TaskCompletionSource release) : IBackgroundTask
    {
        public string Name => "Blocked";
        public string DisplayName => "Blocked task";
        public TaskPriority Priority => TaskPriority.Background;
        public CancellationTokenSource CancellationTokenSource { get; } = new();
        public PauseGate PauseGate { get; } = new();
        public Task RunAsync() => release.Task;
    }

    /// <summary>A prompt service whose question is answered with <paramref name="answer"/>, and which reports when it was asked.</summary>
    private static (Mock<IPromptService> Prompts, TaskCompletionSource Asked) PromptsAnswering(bool? answer)
    {
        var asked = new TaskCompletionSource();
        Mock<IPromptService> prompts = new();
        prompts.Setup(p => p.ConfirmAsync(It.IsAny<ConfirmationPrompt>(), It.IsAny<CancellationToken>()))
            .Callback(() => asked.TrySetResult())
            .ReturnsAsync(answer);
        return (prompts, asked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NoBackgroundTasks_QuitsWithoutAsking()
    {
        // A strict mock fails the test if anything asks the person a question.
        Mock<IPromptService> prompts = new(MockBehavior.Strict);
        using var tasks = new TaskManager(1);
        var command = new QuitWithConfirmationCommand(tasks, prompts.Object);

        await command.ExecuteAsync(null);

        prompts.VerifyNoOtherCalls();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RunningTask_AsksWithNoAsTheDefault()
    {
        var release = new TaskCompletionSource();
        var (prompts, asked) = PromptsAnswering(false);
        using var tasks = new TaskManager(1);
        await tasks.Add(new BlockedTask(release));
        var command = new QuitWithConfirmationCommand(tasks, prompts.Object);

        // The question is answered in the background, so the key handler returns at once.
        await command.ExecuteAsync(null);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(30));

        prompts.Verify(p => p.ConfirmAsync(
            It.Is<ConfirmationPrompt>(prompt => prompt.DefaultValue == false && prompt.Question.Contains("1")),
            It.IsAny<CancellationToken>()), Times.Once);
        release.SetResult();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RunningTask_AcceptedQuitsAfterTheAnswer()
    {
        var release = new TaskCompletionSource();
        var (prompts, asked) = PromptsAnswering(true);
        using var tasks = new TaskManager(1);
        await tasks.Add(new BlockedTask(release));
        var command = new QuitWithConfirmationCommand(tasks, prompts.Object);

        await command.ExecuteAsync(null);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(30));

        prompts.Verify(p => p.ConfirmAsync(It.IsAny<ConfirmationPrompt>(), It.IsAny<CancellationToken>()), Times.Once);
        release.SetResult();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnotherF10WhileTheQuestionIsUp_DoesNotAskAgain()
    {
        var release = new TaskCompletionSource();
        var answer = new TaskCompletionSource<bool?>();
        var asked = new TaskCompletionSource();
        Mock<IPromptService> prompts = new();
        prompts.Setup(p => p.ConfirmAsync(It.IsAny<ConfirmationPrompt>(), It.IsAny<CancellationToken>()))
            .Callback(() => asked.TrySetResult())
            .Returns(answer.Task);
        using var tasks = new TaskManager(1);
        await tasks.Add(new BlockedTask(release));
        var command = new QuitWithConfirmationCommand(tasks, prompts.Object);

        await command.ExecuteAsync(null);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await command.ExecuteAsync(null);
        answer.SetResult(false);

        prompts.Verify(p => p.ConfirmAsync(It.IsAny<ConfirmationPrompt>(), It.IsAny<CancellationToken>()), Times.Once);
        release.SetResult();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutATaskManager_QuitsWithoutAsking()
    {
        Mock<IPromptService> prompts = new(MockBehavior.Strict);
        var command = new QuitWithConfirmationCommand(null, prompts.Object);

        await command.ExecuteAsync(null);

        prompts.VerifyNoOtherCalls();
    }
}
