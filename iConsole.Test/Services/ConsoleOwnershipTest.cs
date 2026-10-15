using iConsole.Services;
using System.Diagnostics;

namespace iConsole.Test.Services;

/// <summary>
/// Lending the terminal to a child process: the loops that write to and read from it stand still,
/// every participant undoes its state first and redoes it after, and the terminal always comes back.
/// </summary>
[TestClass]
public class ConsoleOwnershipTest
{
    private sealed class Recorder(List<string> log, string name) : IConsoleHandoverParticipant
    {
        public void OnConsoleReleased()
        {
            lock (log)
            {
                log.Add($"{name} released");
            }
        }

        public void OnConsoleReacquired()
        {
            lock (log)
            {
                log.Add($"{name} reacquired");
            }
        }
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ParticipantsLetGoInOrderAndTakeBackInReverse()
    {
        List<string> log = [];
        var ownership = new ConsoleOwnership();
        ownership.AddParticipant(new Recorder(log, "screen"));
        ownership.AddParticipant(new Recorder(log, "keyboard"));

        var result = await ownership.LendAsync(() =>
        {
            lock (log)
            {
                log.Add("child");
            }

            return 7;
        });

        Assert.AreEqual(7, result);
        CollectionAssert.AreEqual(
            new[] { "screen released", "keyboard released", "child", "keyboard reacquired", "screen reacquired" },
            log);
        Assert.IsFalse(ownership.IsLent);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task NoPassStartsWhileTheTerminalIsLent()
    {
        var ownership = new ConsoleOwnership();
        bool? refusedDuringLend = null;

        await ownership.LendAsync(() =>
        {
            refusedDuringLend = !ownership.TryBeginUse();
            return 0;
        });

        Assert.IsTrue(refusedDuringLend, "the renderer must not draw over the shell, the reader must not take its keys");
        Assert.IsTrue(ownership.TryBeginUse(), "and they carry on once it is back");
        ownership.EndUse();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ALendWaitsForThePassUnderWay()
    {
        var ownership = new ConsoleOwnership();
        Assert.IsTrue(ownership.TryBeginUse(), "a frame is being written");
        var childStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var lend = ownership.LendAsync(() =>
        {
            childStarted.SetResult();
            return 0;
        });

        await Task.Delay(200);
        Assert.IsFalse(childStarted.Task.IsCompleted, "the child must not start halfway through a frame");

        ownership.EndUse();
        await childStarted.Task;
        await lend;
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task TheTerminalComesBackWhenTheBorrowerThrows()
    {
        List<string> log = [];
        var ownership = new ConsoleOwnership();
        ownership.AddParticipant(new Recorder(log, "screen"));
        var reacquired = 0;
        ownership.Reacquired += (_, _) => reacquired++;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ownership.LendAsync<int>(() => throw new InvalidOperationException("no such program")));

        CollectionAssert.Contains(log, "screen reacquired");
        Assert.AreEqual(1, reacquired);
        Assert.IsFalse(ownership.IsLent);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task LendsAreTakenInTurn()
    {
        var ownership = new ConsoleOwnership();
        var inside = 0;
        var overlapped = false;

        int Borrow()
        {
            if (Interlocked.Increment(ref inside) > 1)
            {
                overlapped = true;
            }

            Thread.Sleep(100);
            Interlocked.Decrement(ref inside);
            return 0;
        }

        await Task.WhenAll(ownership.LendAsync(Borrow), ownership.LendAsync(Borrow), ownership.LendAsync(Borrow));

        Assert.IsFalse(overlapped, "two children on one terminal");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task AProgramRunsOnTheLentTerminalAndItsExitCodeComesBack()
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c exit 3")
            : new ProcessStartInfo("/bin/sh", "-c \"exit 3\"");

        var exitCode = await new ConsoleOwnership().RunInteractiveAsync(startInfo);

        Assert.AreEqual(3, exitCode);
    }

    [TestMethod]
    public void AProgramWhoseOutputIsTakenIsNotInteractive()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ConsoleOwnership.RunInherited(new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true }));
    }
}
