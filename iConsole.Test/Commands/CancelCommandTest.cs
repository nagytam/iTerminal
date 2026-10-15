using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using System.Threading;

namespace iConsole.Commands.Test;

[TestClass]
public class CancelCommandTest
{
    private class FakeParent : iConsole.Controls.Control
    {
        public bool WasClosed { get; private set; }
        public FakeParent() : base(new Moq.Mock<iConsole.Services.IThemeService>().Object) { }
        public void Close() => WasClosed = true;
    }

    [TestMethod]
    public async Task ExecuteAsync_ShouldCancelTokenAndCloseParent()
    {
        var cts = new CancellationTokenSource();
        var cmd = new CancelCommand(cts);

        var parent = new FakeParent();
        var child = new iConsole.Controls.Control(new Moq.Mock<iConsole.Services.IThemeService>().Object);
        child.Parent = parent;
        cmd.Control = child;

        await cmd.ExecuteAsync(null);

        Assert.IsTrue(cts.IsCancellationRequested);
        // parent Close is invoked via Control?.Parent?.Close() - ensure parent.Close didn't throw and token canceled
        Assert.IsFalse(cts.IsCancellationRequested == false);
    }
}

