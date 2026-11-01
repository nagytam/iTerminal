using iConsole.Data;
using iTerminal.Core.Services.VirtualMachines;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class VirtualMachineManagerDialogTest
{
    /// <summary>A service that records what the dialog asked for and answers from a list it is given.</summary>
    private sealed class FakeService(params VirtualMachine[] machines) : IVirtualMachineService
    {
        private readonly object _lock = new();
        private readonly List<(string Name, VirtualMachineAction Action)> _calls = [];
        private TaskCompletionSource<IReadOnlyList<VirtualMachine>>? _gate;

        public volatile bool ListEntered;
        public VirtualMachineActionResult Result { get; set; } = new(true, "done");

        public IReadOnlyList<(string Name, VirtualMachineAction Action)> Calls
        {
            get { lock (_lock) { return _calls.ToList(); } }
        }

        /// <summary>Holds the next listing until the test releases it.</summary>
        public void HoldNextListing() => _gate = new TaskCompletionSource<IReadOnlyList<VirtualMachine>>();

        public void ReleaseListing() => _gate?.TrySetResult(machines);

        public Task<IReadOnlyList<VirtualMachine>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListEntered = true;
            return _gate is { } gate ? gate.Task : Task.FromResult<IReadOnlyList<VirtualMachine>>(machines);
        }

        public Task<VirtualMachineActionResult> RunAsync(string name, VirtualMachineAction action, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _calls.Add((name, action));
            }

            return Task.FromResult(Result);
        }
    }

    private static readonly VirtualMachine Running = new("web", VirtualMachineState.Running, "running");
    private static readonly VirtualMachine ShutOff = new("web", VirtualMachineState.ShutOff, "shut off");

    private static async Task<VirtualMachineManagerDialog> OpenAsync(FakeService service)
    {
        var dialog = new VirtualMachineManagerDialog(service, TestTheme());
        await ShowAsync(dialog);
        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.SelectedMachine is not null;
        }, "the machine list to arrive");
        return dialog;
    }

    private static Task<bool> Press(VirtualMachineManagerDialog dialog, char keyChar, ConsoleKey key, bool shift = false) =>
        dialog.HandleKeyEventAsync(new KeyEvent(keyChar, key, shift, false, false));

    // Reverted (Enter no longer starts a shut-off machine): the service is never asked and this fails.
    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterStartsTheShutOffMachineUnderTheCursor()
    {
        var service = new FakeService(ShutOff);
        var dialog = await OpenAsync(service);

        await Press(dialog, '\r', ConsoleKey.Enter);
        await dialog.PendingOperation;

        CollectionAssert.AreEqual(new[] { ("web", VirtualMachineAction.Start) }, service.Calls.ToArray());
    }

    // Reverted (Enter starts anything): a running machine is started again and this fails.
    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterDoesNothingToARunningMachine()
    {
        var service = new FakeService(Running);
        var dialog = await OpenAsync(service);

        await Press(dialog, '\r', ConsoleKey.Enter);
        await dialog.PendingOperation;

        Assert.AreEqual(0, service.Calls.Count);
    }

    // Reverted (D runs without asking): the shutdown happens before the answer and this fails.
    [Timeout(60_000)]
    [TestMethod]
    public async Task DecliningTheShutdownLeavesTheMachineAlone()
    {
        var service = new FakeService(Running);
        var dialog = await OpenAsync(service);

        await Press(dialog, 'd', ConsoleKey.D);
        Assert.AreEqual(0, service.Calls.Count, "nothing may run before the answer");
        Assert.IsNotNull(dialog.AwaitingConfirmation);

        await Press(dialog, 'n', ConsoleKey.N);
        await dialog.PendingOperation;

        Assert.AreEqual(0, service.Calls.Count);
        Assert.IsNull(dialog.AwaitingConfirmation);
        Assert.AreEqual("Cancelled.", dialog.Status);
    }

    // Reverted (the answer is ignored, or the confirmation is skipped): the shutdown does not run and this fails.
    [Timeout(60_000)]
    [TestMethod]
    public async Task ConfirmingTheShutdownRunsIt()
    {
        var service = new FakeService(Running);
        var dialog = await OpenAsync(service);

        await Press(dialog, 'd', ConsoleKey.D);
        await Press(dialog, 'y', ConsoleKey.Y);
        await dialog.PendingOperation;

        CollectionAssert.AreEqual(new[] { ("web", VirtualMachineAction.Shutdown) }, service.Calls.ToArray());
    }

    // Reverted (Shift is not checked, so a plain x powers off): the first press would ask and this fails.
    [Timeout(60_000)]
    [TestMethod]
    public async Task PowerOffNeedsShiftAndAnswer()
    {
        var service = new FakeService(Running);
        var dialog = await OpenAsync(service);

        await Press(dialog, 'x', ConsoleKey.X);
        Assert.IsNull(dialog.AwaitingConfirmation, "a plain x is not power off");

        await Press(dialog, 'X', ConsoleKey.X, shift: true);
        Assert.AreEqual(VirtualMachineAction.PowerOff, dialog.AwaitingConfirmation!.Value.Action);
        Assert.AreEqual(0, service.Calls.Count);
    }

    // Reverted (pause asks first): the pause would wait for an answer and this fails.
    [Timeout(60_000)]
    [TestMethod]
    public async Task PauseActsAtOnceWithoutAsking()
    {
        var service = new FakeService(Running);
        var dialog = await OpenAsync(service);

        await Press(dialog, 'p', ConsoleKey.P);
        await dialog.PendingOperation;

        CollectionAssert.AreEqual(new[] { ("web", VirtualMachineAction.Pause) }, service.Calls.ToArray());
    }

    // Reverted (the worker writes the list itself, off the UI thread): the list is filled before OnIdle and this fails.
    [Timeout(60_000)]
    [TestMethod]
    public async Task AListingReachesTheListOnlyThroughOnIdle()
    {
        var service = new FakeService(Running);
        service.HoldNextListing();
        var dialog = new VirtualMachineManagerDialog(service, TestTheme());
        await ShowAsync(dialog);
        await WaitUntilAsync(() => service.ListEntered, "the listing to start");

        service.ReleaseListing();
        await Task.Delay(100);

        Assert.IsNull(dialog.SelectedMachine, "a finished listing waits for OnIdle");
        dialog.OnIdle();
        Assert.IsNotNull(dialog.SelectedMachine);
    }

    // Reverted (the outcome is not written back): the status line keeps "Working on" and this fails.
    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedActionReportsItsMessage()
    {
        var service = new FakeService(Running) { Result = new VirtualMachineActionResult(false, "web: the hypervisor did not answer in time.") };
        var dialog = await OpenAsync(service);

        await Press(dialog, 'p', ConsoleKey.P);
        await dialog.PendingOperation;
        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.Status.StartsWith("web:", StringComparison.Ordinal);
        }, "the outcome to reach the status line");

        Assert.AreEqual("web: the hypervisor did not answer in time.", dialog.Status);
    }
}
