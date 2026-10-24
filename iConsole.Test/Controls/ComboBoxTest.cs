using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ComboBoxTest
{
    private static Mock<IThemeService> ThemeMock()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return themeService;
    }

    private sealed record Harness(Window Root, ComboBox<string> Combo);

    /// <summary>A combo box mounted under a root, ready for the dropdown popup to attach.</summary>
    private static Harness CreateHarness()
    {
        var theme = ThemeMock().Object;
        var root = new Window(theme) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        var combo = new ComboBox<string>(theme) { Position = new Position(2, 2), ViewSize = new Size(20, 1) };
        root.AddControl(combo);
        combo.SetItems(["Red", "Green", "Blue"]);
        combo.SetFocus();
        return new Harness(root, combo);
    }

    /// <summary>
    /// A key the focused control declines is offered to every control. An unfocused combo used to
    /// answer Enter by opening its list — over a dialog whose own Enter was meant to run.
    /// </summary>
    [TestMethod]
    public async Task AnUnfocusedComboIgnoresEnterAndTheArrows()
    {
        var harness = CreateHarness();
        harness.Root.ResetFocus();

        Assert.IsFalse(await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.Enter)));
        Assert.IsFalse(await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.DownArrow)));
        Assert.IsFalse(harness.Combo.IsOpen);
        Assert.AreEqual(0, harness.Combo.SelectedIndex);
    }

    private static Popup OpenPopup(Window root) =>
        root.Controls.AsSnapshot().OfType<Popup>().Single();

    private static KeyEvent Key(ConsoleKey consoleKey, bool alt = false) => new('\0', consoleKey, false, alt, false);

    [TestMethod]
    public void SetItemsSelectsFirstItem()
    {
        var combo = new ComboBox<string>(ThemeMock().Object);
        combo.SetItems(["Red", "Green", "Blue"]);

        Assert.AreEqual(0, combo.SelectedIndex);
        Assert.AreEqual("Red", combo.SelectedItem);
    }

    [TestMethod]
    public async Task ClosedArrowsCycleSelectionWithoutOpening()
    {
        var harness = CreateHarness();

        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.IsFalse(harness.Combo.IsOpen);
        Assert.AreEqual(1, harness.Combo.SelectedIndex);

        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(0, harness.Combo.SelectedIndex);
    }

    [TestMethod]
    public async Task EnterOpensTheDropdownPopup()
    {
        var harness = CreateHarness();

        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsTrue(harness.Combo.IsOpen);
        Assert.IsTrue(OpenPopup(harness.Root).IsOpen);
    }

    [TestMethod]
    public async Task AltDownOpensWithoutMovingSelection()
    {
        var harness = CreateHarness();

        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.DownArrow, alt: true));

        Assert.IsTrue(harness.Combo.IsOpen);
        Assert.AreEqual(0, harness.Combo.SelectedIndex);
    }

    [TestMethod]
    public async Task CommittingInThePopupUpdatesSelectionAndCloses()
    {
        var harness = CreateHarness();
        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        var popup = OpenPopup(harness.Root);

        // The popup is modal and routes to its hosted list; move down and commit.
        await popup.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await popup.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsFalse(harness.Combo.IsOpen);
        Assert.AreEqual(1, harness.Combo.SelectedIndex);
        Assert.AreEqual("Green", harness.Combo.SelectedItem);
    }

    [TestMethod]
    public async Task EscapeInThePopupClosesWithoutChangingSelection()
    {
        var harness = CreateHarness();
        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        var popup = OpenPopup(harness.Root);

        await popup.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(harness.Combo.IsOpen);
        Assert.AreEqual(0, harness.Combo.SelectedIndex);
    }

    [TestMethod]
    public async Task ClickOpensTheDropdown()
    {
        var harness = CreateHarness();

        // Combo sits at (2,2); click its closed line.
        await harness.Combo.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 2), LeftClicked = true });

        Assert.IsTrue(harness.Combo.IsOpen);
    }

    [TestMethod]
    public async Task SelectionChangedFiresOnCommitOnly()
    {
        var harness = CreateHarness();
        var raised = 0;
        harness.Combo.SelectionChanged += (_, _) => raised++;

        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        var popup = OpenPopup(harness.Root);
        await popup.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(0, raised); // moving the highlight in the popup must not change selection

        await popup.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public async Task ReadOnlyIgnoresInput()
    {
        var harness = CreateHarness();
        harness.Combo.IsReadOnly = true;

        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.IsFalse(harness.Combo.IsOpen);

        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(0, harness.Combo.SelectedIndex);
    }

    [TestMethod]
    public void DrawClosedShowsSelectedItem()
    {
        var buffer = new MockBuffer(new Size(20, 1));
        var combo = new ComboBox<string>(ThemeMock().Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };
        combo.SetItems(["Red", "Green", "Blue"]);

        combo.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Red"));
        Assert.IsTrue(buffer.ContentAsString.Contains("▾"));
    }

    [TestMethod]
    public async Task ClosedPageKeys_MoveTheSelectionByAPage_AndStopAtTheEnds()
    {
        var theme = ThemeMock().Object;
        var root = new Window(theme) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        var combo = new ComboBox<string>(theme) { Position = new Position(2, 2), ViewSize = new Size(20, 1) };
        root.AddControl(combo);
        combo.SetItems(Enumerable.Range(0, 12).Select(i => $"item{i}").ToList());
        combo.SetFocus();

        Assert.IsTrue(await combo.HandleKeyEventAsync(Key(ConsoleKey.PageDown)));
        Assert.AreEqual(5, combo.SelectedIndex);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        await combo.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(11, combo.SelectedIndex);
        Assert.IsFalse(combo.IsOpen);

        await combo.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(6, combo.SelectedIndex);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        await combo.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(0, combo.SelectedIndex);
    }

    [TestMethod]
    public async Task ClosedPageKeys_ReadOnlyOrUnfocused_AreIgnored()
    {
        var harness = CreateHarness();
        harness.Combo.IsReadOnly = true;

        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(0, harness.Combo.SelectedIndex);

        harness.Combo.IsReadOnly = false;
        harness.Root.ResetFocus();
        Assert.IsFalse(await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.PageDown)));
        Assert.AreEqual(0, harness.Combo.SelectedIndex);
    }

    [TestMethod]
    public async Task ClosedPageKeys_OnAShortList_GoToTheLastItem()
    {
        var harness = CreateHarness();

        await harness.Combo.HandleKeyEventAsync(Key(ConsoleKey.PageDown));

        Assert.AreEqual(2, harness.Combo.SelectedIndex);
    }
}
