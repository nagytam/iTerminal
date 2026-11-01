using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>HexViewer mouse and sizing, ListBox refresh and colours, StrengthMeter ratings.</summary>
[TestClass]
public class HexListAndStrengthInteractionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    // ---- HexViewer ----

    private static HexViewer NewHex(int bytes = 160, int height = 4)
    {
        HexViewer viewer = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, height) };
        viewer.OnInitializeLayout();
        viewer.Data = [.. Enumerable.Range(0, bytes).Select(i => (byte)i)];
        return viewer;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HexViewer_ClickRow_FocusesAndMovesTheCursor()
    {
        HexViewer viewer = NewHex();
        await viewer.HandleMouseEventAsync(Click(5, 2));
        Assert.IsTrue(viewer.IsFocused);
        Assert.AreEqual(32, viewer.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HexViewer_Wheel_MovesThreeRowsAndClamps()
    {
        HexViewer viewer = NewHex();
        await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 1), WheelDelta = -1 });
        Assert.AreEqual(48, viewer.SelectedOffset);
        await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 1), WheelDelta = -1 });
        await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 1), WheelDelta = -1 });
        Assert.AreEqual(144, viewer.SelectedOffset);
        await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 1), WheelDelta = 1 });
        Assert.AreEqual(96, viewer.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HexViewer_ClickPastTheDataOrOutside_IsIgnored()
    {
        HexViewer viewer = NewHex(bytes: 16, height: 4);
        Assert.IsFalse(await viewer.HandleMouseEventAsync(Click(5, 3)));
        Assert.IsFalse(await viewer.HandleMouseEventAsync(Click(200, 0)));
        Assert.AreEqual(0, viewer.SelectedOffset);
        HexViewer empty = NewHex(bytes: 0);
        Assert.IsFalse(await empty.HandleMouseEventAsync(Click(5, 0)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HexViewer_BytesPerRow_NeverDropsBelowOne()
    {
        HexViewer viewer = NewHex(bytes: 10);
        viewer.BytesPerRow = 0;
        Assert.AreEqual(1, viewer.BytesPerRow);
        viewer.Draw(new MockBuffer(new Size(80, 4)));
        viewer.BytesPerRow = 8;
        Assert.AreEqual(8, viewer.BytesPerRow);
        viewer.Draw(new MockBuffer(new Size(80, 4)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HexViewer_Draw_BlanksRowsPastTheData()
    {
        HexViewer viewer = NewHex(bytes: 16, height: 3);
        MockBuffer buffer = new(new Size(80, 3));
        viewer.Draw(buffer);
        string[] lines = Lines(buffer);
        Assert.IsTrue(lines[0].StartsWith("00000000"));
        Assert.AreEqual(string.Empty, lines[1].Trim());
    }

    // ---- ListBox ----

    private static (ListBox<string> List, ConcurrentObservableCollection<string> Items, ListBoxModel<string> Model) NewList(params string[] items)
    {
        ConcurrentObservableCollection<string> collection = new(items);
        ListBoxModel<string> model = new(collection);
        model.UpdateListItems();
        ListBox<string> list = new(model, Theme()) { Position = new Position(0, 0), ViewSize = new Size(12, 3) };
        list.OnInitializeLayout();
        return (list, collection, model);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListBox_RefreshItems_ReprojectsRowsAfterAFilterChange()
    {
        (ListBox<string> list, _, ListBoxModel<string> model) = NewList("apple", "banana", "avocado");
        model.FilterAndSortItems = (items, _) => items.Where(i => i.StartsWith('a'));
        list.RefreshItems();
        Assert.AreEqual(2, model.ListItems.Count);
        MockBuffer buffer = new(new Size(12, 3));
        list.Draw(buffer);
        Assert.IsFalse(buffer.ContentAsString.Contains("banana"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListBox_OnIdle_PicksUpItemsAddedFromAnotherThread()
    {
        (ListBox<string> list, ConcurrentObservableCollection<string> items, ListBoxModel<string> model) = NewList("one");
        items.Add("two");
        list.OnIdle();
        Assert.AreEqual(2, model.ListItems.Count);
        Assert.IsFalse(model.HasPendingUpdate);
        list.OnIdle();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListBox_Colors_CoverEveryVisualState()
    {
        (ListBox<string> list, _, _) = NewList("one");
        ColorScheme scheme = new();
        list.GetColor(scheme, VisualState.Disabled, out Color disabledFg, out _);
        Assert.AreEqual(scheme.DisabledForeground, disabledFg);
        list.GetColor(scheme, VisualState.SelectedDisabled, out Color selectedDisabledFg, out _);
        Assert.AreEqual(scheme.DisabledSelectedForeground, selectedDisabledFg);
        list.GetColor(scheme, VisualState.Selected, out Color quietFg, out _);
        Assert.AreEqual(scheme.SelectedForeground, quietFg);
        list.SetFocus();
        list.GetColor(scheme, VisualState.Selected, out Color focusedFg, out _);
        Assert.AreEqual(scheme.SelectedFocusedForeground, focusedFg);
        list.GetColor(scheme, VisualState.Normal, out Color normalFg, out _);
        Assert.AreEqual(scheme.Foreground, normalFg);
    }

    // ---- StrengthMeter ----

    [Timeout(60_000)]
    [TestMethod]
    public void StrengthMeter_Rate_OrdersFromWeakToStrong()
    {
        Assert.AreEqual(PasswordStrength.None, StrengthMeter.Rate(null));
        Assert.AreEqual(PasswordStrength.None, StrengthMeter.Rate(string.Empty));
        Assert.AreEqual(PasswordStrength.Weak, StrengthMeter.Rate("abc"));
        Assert.AreEqual(PasswordStrength.Weak, StrengthMeter.Rate("Password"));
        Assert.AreEqual(PasswordStrength.Weak, StrengthMeter.Rate("aaaaaaaaaa"));
        Assert.AreEqual(PasswordStrength.Weak, StrengthMeter.Rate("abcdefgh"));
        Assert.AreEqual(PasswordStrength.Strong, StrengthMeter.Rate("Tr0ub4dor&3-horse-Staple"));
        Assert.IsTrue(StrengthMeter.Rate("Horse9Staple") > StrengthMeter.Rate("horsestaple"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StrengthMeter_Render_ShowsBarAndName()
    {
        Assert.AreEqual("▱▱▱▱", StrengthMeter.Render(PasswordStrength.None));
        Assert.AreEqual("▰▱▱▱ Weak", StrengthMeter.Render(PasswordStrength.Weak));
        Assert.AreEqual("▰▰▰▱ Good", StrengthMeter.Render(PasswordStrength.Good));
        Assert.AreEqual("▰▰▰▰ Strong", StrengthMeter.Render(PasswordStrength.Strong));
        Assert.AreEqual(Color.Red, StrengthMeter.ColorOf(PasswordStrength.Weak, Color.Gray));
        Assert.AreEqual(Color.Gray, StrengthMeter.ColorOf(PasswordStrength.None, Color.Gray));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StrengthMeter_Password_UpdatesTheRatingAndDrawsWithoutKeepingTheText()
    {
        StrengthMeter meter = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(12, 1) };
        meter.Password = "Tr0ub4dor&3-horse-Staple";
        Assert.AreEqual(PasswordStrength.Strong, meter.Strength);
        MockBuffer buffer = new(new Size(12, 1));
        meter.Draw(buffer);
        Assert.AreEqual("▰▰▰▰ Strong", Lines(buffer)[0].TrimEnd());
        meter.ViewSize = new Size(6, 1);
        MockBuffer narrow = new(new Size(6, 1));
        meter.Draw(narrow);
        Assert.AreEqual("▰▰▰▰ S", Lines(narrow)[0]);
        meter.Password = string.Empty;
        Assert.AreEqual(PasswordStrength.None, meter.Strength);
    }
}
