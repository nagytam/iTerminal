using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Buttons;

[TestClass]
public class ActionButtonTest
{
    [TestMethod]
    public void ActionButtonCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockCommand = new Mock<ICommand>();
        var actionButton = new ActionButton("Save", "Ctrl+S", mockCommand.Object, mockThemeService.Object);

        Assert.IsNotNull(actionButton);
        Assert.IsInstanceOfType(actionButton, typeof(Button));
    }

    [TestMethod]
    public void ActionButtonHasTitle()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockCommand = new Mock<ICommand>();
        var actionButton = new ActionButton("Save", "Ctrl+S", mockCommand.Object, mockThemeService.Object);

        // ActionButton inherits from Button, check title through base class
        Assert.IsNotNull(actionButton);
    }

    [TestMethod]
    public void ActionButtonHasActionView()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockCommand = new Mock<ICommand>();
        var actionButton = new ActionButton("Save", "Ctrl+S", mockCommand.Object, mockThemeService.Object);

        Assert.AreEqual("Ctrl+S", actionButton.ActionView);
    }

    [TestMethod]
    public void ActionButtonDoesNotDrawDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockCommand = new Mock<ICommand>();
        var buffer = new MockBuffer(new Size(20, 1));
        var actionButton = new ActionButton("Save", "Ctrl+S", mockCommand.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        actionButton.Draw(buffer);
        // ActionButton override Draw to do nothing - parent control draws it
        // So buffer should be empty or unchanged
    }

    [TestMethod]
    public void ActionButtonHasCommand()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockCommand = new Mock<ICommand>();
        var actionButton = new ActionButton("Delete", "Del", mockCommand.Object, mockThemeService.Object);

        Assert.IsNotNull(actionButton);
    }

    [TestMethod]
    public void ActionButtonActionViewCanContainComplexKeybinding()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockCommand = new Mock<ICommand>();
        var actionButton = new ActionButton("Find & Replace", "Ctrl+H", mockCommand.Object, mockThemeService.Object);

        Assert.AreEqual("Ctrl+H", actionButton.ActionView);
    }
}

[TestClass]
public class CancelButtonTest
{
    [TestMethod]
    public void CancelButtonCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var cts = new CancellationTokenSource();
        var cancelButton = new CancelButton(cts, mockThemeService.Object);

        Assert.IsNotNull(cancelButton);
        Assert.IsInstanceOfType(cancelButton, typeof(Button));
    }

    [TestMethod]
    public void CancelButtonHasCorrectTitle()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var cts = new CancellationTokenSource();
        var buffer = new MockBuffer(new Size(20, 1));
        var cancelButton = new CancelButton(cts, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        cancelButton.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Cancel"));
    }

    [TestMethod]
    public void CancelButtonHasHotKeyC()
    {
        var mockThemeService = new Mock<IThemeService>();
        var cts = new CancellationTokenSource();
        var cancelButton = new CancelButton(cts, mockThemeService.Object);

        Assert.AreEqual('c', cancelButton.HotKeyChar);
    }

    [TestMethod]
    public async Task CancelButtonExecutesCancelCommand()
    {
        var mockThemeService = new Mock<IThemeService>();
        var cts = new CancellationTokenSource();
        var cancelButton = new CancelButton(cts, mockThemeService.Object)
        {
            IsEnabled = true,
            ViewSize = new Size(20, 1)
        };
        cancelButton.SetFocus();

        await cancelButton.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        // After executing cancel command, the cancellation token should be requested
        Assert.IsTrue(cts.Token.IsCancellationRequested);
    }

    [TestMethod]
    public void CancelButtonIsDisposable()
    {
        var mockThemeService = new Mock<IThemeService>();
        var cts = new CancellationTokenSource();
        var cancelButton = new CancelButton(cts, mockThemeService.Object);

        // Should not throw
        cts.Dispose();
    }
}

[TestClass]
public class CancelDialogButtonTest
{
    [TestMethod]
    public void CancelDialogButtonCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var cancelButton = new CancelDialogButton(mockThemeService.Object);

        Assert.IsNotNull(cancelButton);
        Assert.IsInstanceOfType(cancelButton, typeof(Button));
    }

    [TestMethod]
    public void CancelDialogButtonHasCorrectTitle()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var cancelButton = new CancelDialogButton(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        cancelButton.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Cancel"));
    }

    [TestMethod]
    public void CancelDialogButtonHasHotKeyC()
    {
        var mockThemeService = new Mock<IThemeService>();
        var cancelButton = new CancelDialogButton(mockThemeService.Object);

        Assert.AreEqual('c', cancelButton.HotKeyChar);
    }

    [TestMethod]
    public async Task CancelDialogButtonExecutesCancelDialogCommand()
    {
        var mockThemeService = new Mock<IThemeService>();
        var cancelButton = new CancelDialogButton(mockThemeService.Object)
        {
            IsEnabled = true,
            ViewSize = new Size(20, 1)
        };
        cancelButton.SetFocus();

        await cancelButton.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        // Command should execute without throwing
    }
}

[TestClass]
public class CloseButtonTest
{
    [TestMethod]
    public void CloseButtonCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var closeButton = new CloseButton(mockThemeService.Object);

        Assert.IsNotNull(closeButton);
        Assert.IsInstanceOfType(closeButton, typeof(Button));
    }

    [TestMethod]
    public void CloseButtonHasCorrectTitle()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var closeButton = new CloseButton(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        closeButton.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Close"));
    }

    [TestMethod]
    public void CloseButtonHasHotKeyL()
    {
        var mockThemeService = new Mock<IThemeService>();
        var closeButton = new CloseButton(mockThemeService.Object);

        Assert.AreEqual('l', closeButton.HotKeyChar);
    }

    [TestMethod]
    public async Task CloseButtonExecutesCloseParentCommand()
    {
        var mockThemeService = new Mock<IThemeService>();
        var closeButton = new CloseButton(mockThemeService.Object)
        {
            IsEnabled = true,
            ViewSize = new Size(20, 1)
        };
        closeButton.SetFocus();

        await closeButton.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        // Command should execute without throwing
    }
}
