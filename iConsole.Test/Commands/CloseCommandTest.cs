using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Controls;

namespace iConsole.Commands.Test;

[TestClass]
public class CloseCommandTest
{
    private class FakeControl : Control
    {
        public bool WasClosed { get; private set; }
        public FakeControl() : base(new Moq.Mock<iConsole.Services.IThemeService>().Object) { }
        public override void Close() { WasClosed = true; }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_ShouldCloseControl()
    {
        var cmd = new CloseCommand();
        var ctrl = new FakeControl();
        cmd.Control = ctrl;

        await cmd.ExecuteAsync(null);

        Assert.IsTrue(ctrl.WasClosed);
    }
}

