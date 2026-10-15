using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Inputs;
using iConsole.Jobs;
using iConsole.Services;
using Moq;

namespace iConsole.Jobs.Test;

[TestClass]
public class TerminalInputConsumerJobTest
{
    [TestMethod]
    public void Constructor_SetsInputPriority()
    {
        Mock<ITerminalInputSource> inputSource = new();
        inputSource.SetupGet(source => source.MouseQueue).Returns(new System.Collections.Concurrent.ConcurrentQueue<iConsole.Data.MouseEvent>());
        inputSource.SetupGet(source => source.KeyQueue).Returns(new System.Collections.Concurrent.ConcurrentQueue<iConsole.Data.KeyEvent>());
        inputSource.SetupGet(source => source.InputAvailableSignal).Returns(new SemaphoreSlim(0));
        Mock<IBeepService> beepService = new();

        TerminalInputConsumerJob job = new(inputSource.Object, beepService.Object);

        Assert.AreEqual(JobPriority.Input, job.Priority);
    }

    /// <summary>
    /// A handler that throws must cost that one key, not the keyboard: the exception used to end the
    /// job, and every key after it was silently dropped while the app went on drawing.
    /// </summary>
    [TestMethod]
    [Timeout(10_000)]
    public async Task AHandlerThatThrows_DoesNotStopTheKeyboard()
    {
        var keys = new System.Collections.Concurrent.ConcurrentQueue<iConsole.Data.KeyEvent>();
        var signal = new SemaphoreSlim(0);
        Mock<ITerminalInputSource> inputSource = new();
        inputSource.SetupGet(source => source.MouseQueue).Returns(new System.Collections.Concurrent.ConcurrentQueue<iConsole.Data.MouseEvent>());
        inputSource.SetupGet(source => source.KeyQueue).Returns(keys);
        inputSource.SetupGet(source => source.InputAvailableSignal).Returns(signal);

        var secondKeyHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<iConsole.Controls.IControl> control = new();
        control.Setup(c => c.HandleKeyEventAsync(It.Is<iConsole.Data.KeyEvent>(k => k.ConsoleKey == ConsoleKey.F3)))
            .ThrowsAsync(new InvalidOperationException("broken handler"));
        control.Setup(c => c.HandleKeyEventAsync(It.Is<iConsole.Data.KeyEvent>(k => k.ConsoleKey == ConsoleKey.F4)))
            .Callback(() => secondKeyHandled.TrySetResult())
            .ReturnsAsync(true);
        Mock<iConsole.Desktops.IDesktop> desktop = new();
        desktop.Setup(d => d.GetModalChildControl()).Returns(control.Object);

        TerminalInputConsumerJob job = new(inputSource.Object, Mock.Of<IBeepService>());
        job.OnInitialize(desktop.Object);
        using var cancellation = new CancellationTokenSource();
        var running = Task.Run(() => job.ExecuteAsync(cancellation.Token));

        keys.Enqueue(new iConsole.Data.KeyEvent('\0', ConsoleKey.F3, false, false, false));
        signal.Release();
        keys.Enqueue(new iConsole.Data.KeyEvent('\0', ConsoleKey.F4, false, false, false));
        signal.Release();

        await secondKeyHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await running;
    }
}

