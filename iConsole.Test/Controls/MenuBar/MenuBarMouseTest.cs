using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;
using Moq;
using MenuBarControl = iConsole.Controls.MenuBar;

namespace iConsole.Test.Controls.MenuBar;

/// <summary>The menu bar answers the mouse: titles open drop-downs, entries run, outside clicks close.</summary>
[TestClass]
public class MenuBarMouseTest
{
    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private sealed class Rig
    {
        public required MenuBarControl Bar { get; init; }
        public required MenuItem File { get; init; }
        public required MenuItem Edit { get; init; }
        public required MenuItem Quit { get; init; }
        public required MenuItem Recent { get; init; }
        public required MenuItem Nested { get; init; }
        public required Mock<ICommand> QuitCommand { get; init; }
    }

    private static Rig NewRig()
    {
        TestThemeService theme = new();
        Mock<ICommand> quitCommand = new();
        quitCommand.SetupGet(c => c.Key).Returns(new Key(ConsoleKey.Q, false, false, true));
        quitCommand.Setup(c => c.CanExecute()).Returns(true);
        quitCommand.Setup(c => c.RunAsync(null)).ReturnsAsync(true);

        MenuItem nested = new("Nested file", null, [], theme);
        MenuItem recent = new("Recent", null, [nested], theme);
        MenuItem quit = new("Quit", quitCommand.Object, [], theme);
        MenuItem file = new("File", null, [recent, new MenuItem("-", null, [], theme), quit], theme) { HotKeyChar = 'F' };
        MenuItem edit = new("Edit", null, [new MenuItem("Copy", null, [], theme)], theme) { HotKeyChar = 'E' };
        MenuBarControl bar = new([file, edit], theme) { ViewSize = new Size(80, 1), Position = new Position(0, 0) };
        Control root = new(theme) { ViewSize = new Size(80, 25) };
        root.AddControl(bar);
        return new Rig { Bar = bar, File = file, Edit = edit, Quit = quit, Recent = recent, Nested = nested, QuitCommand = quitCommand };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnATitle_OpensItsDropDownAndMakesTheBarModal()
    {
        Rig rig = NewRig();
        Assert.IsTrue(await rig.Bar.HandleMouseEventAsync(Click(1, 0)));
        Assert.IsTrue(rig.File.IsOpened);
        Assert.IsTrue(rig.Bar.IsModal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnAnotherTitle_SwitchesMenus()
    {
        Rig rig = NewRig();
        await rig.Bar.HandleMouseEventAsync(Click(1, 0));
        await rig.Bar.HandleMouseEventAsync(Click(6, 0));
        Assert.IsFalse(rig.File.IsOpened);
        Assert.IsTrue(rig.Edit.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnTheOpenTitle_ClosesTheMenu()
    {
        Rig rig = NewRig();
        await rig.Bar.HandleMouseEventAsync(Click(1, 0));
        await rig.Bar.HandleMouseEventAsync(Click(1, 0));
        Assert.IsFalse(rig.File.IsOpened);
        Assert.IsFalse(rig.Bar.IsModal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnALeafEntry_RunsItsCommandAndClosesTheMenu()
    {
        Rig rig = NewRig();
        await rig.Bar.HandleMouseEventAsync(Click(1, 0));
        // Row 1 is the top border, then Recent, the separator and Quit.
        long quitRow = rig.File.AbsolutePosition.Y + 3;
        Assert.IsTrue(await rig.Bar.HandleMouseEventAsync(Click(rig.File.AbsolutePosition.X + 2, quitRow)));
        rig.QuitCommand.Verify(c => c.RunAsync(null), Times.Once);
        Assert.IsFalse(rig.File.IsOpened);
        Assert.IsFalse(rig.Bar.IsModal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnABranchEntry_OpensItsSubmenu()
    {
        Rig rig = NewRig();
        await rig.Bar.HandleMouseEventAsync(Click(1, 0));
        await rig.Bar.HandleMouseEventAsync(Click(rig.File.AbsolutePosition.X + 2, rig.File.AbsolutePosition.Y + 1));
        Assert.IsTrue(rig.Recent.IsOpened);
        Assert.IsTrue(rig.File.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnASeparatorOrTheBorder_KeepsTheMenuOpen()
    {
        Rig rig = NewRig();
        await rig.Bar.HandleMouseEventAsync(Click(1, 0));
        Assert.IsTrue(await rig.Bar.HandleMouseEventAsync(Click(rig.File.AbsolutePosition.X + 2, rig.File.AbsolutePosition.Y + 2)));
        Assert.IsTrue(await rig.Bar.HandleMouseEventAsync(Click(rig.File.AbsolutePosition.X, rig.File.AbsolutePosition.Y + 1)));
        Assert.IsTrue(await rig.Bar.HandleMouseEventAsync(Click(rig.File.AbsolutePosition.X + 2, rig.File.AbsolutePosition.Y)));
        Assert.IsTrue(rig.File.IsOpened);
        rig.QuitCommand.Verify(c => c.RunAsync(null), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOutsideAnOpenMenu_ClosesItAndIsSwallowed()
    {
        Rig rig = NewRig();
        await rig.Bar.HandleMouseEventAsync(Click(1, 0));
        Assert.IsTrue(await rig.Bar.HandleMouseEventAsync(Click(70, 12)));
        Assert.IsFalse(rig.File.IsOpened);
        Assert.IsFalse(rig.Bar.IsModal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOffTheTitlesWithNothingOpen_IsNotHandled()
    {
        Rig rig = NewRig();
        Assert.IsFalse(await rig.Bar.HandleMouseEventAsync(Click(70, 0)));
        Assert.IsFalse(await rig.Bar.HandleMouseEventAsync(Click(1, 5)));
        Assert.IsFalse(rig.File.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OtherMouseButtonsAndTheWheel_DoNotOpenAMenu()
    {
        Rig rig = NewRig();
        await rig.Bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), RightClicked = true });
        await rig.Bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), WheelDelta = 1 });
        Assert.IsFalse(rig.File.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnTheCloseButtonInTheBorder_ClosesTheMenu()
    {
        Rig rig = NewRig();
        await rig.Bar.HandleMouseEventAsync(Click(1, 0));
        long x = rig.File.AbsolutePosition.X + rig.File.ViewSize.Width - 3;
        Assert.IsTrue(await rig.Bar.HandleMouseEventAsync(Click(x, rig.File.AbsolutePosition.Y)));
        Assert.IsFalse(rig.File.IsOpened);
        Assert.IsFalse(rig.Bar.IsModal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnTheBorderBesideTheCloseButton_KeepsTheMenuOpen()
    {
        Rig rig = NewRig();
        await rig.Bar.HandleMouseEventAsync(Click(1, 0));
        Assert.IsTrue(await rig.Bar.HandleMouseEventAsync(Click(rig.File.AbsolutePosition.X + 1, rig.File.AbsolutePosition.Y)));
        Assert.IsTrue(rig.File.IsOpened);
    }
}
