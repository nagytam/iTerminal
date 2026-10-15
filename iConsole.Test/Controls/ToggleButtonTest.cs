using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ToggleButtonTest
{
    private static Mock<IThemeService> ThemeMock()
    {
        var m = new Mock<IThemeService>();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m;
    }

    private static ToggleButton Create(bool isOn = false, int width = 10)
    {
        return new ToggleButton(ThemeMock().Object)
        {
            IsOn = isOn,
            Position = new Position(0, 0),
            ViewSize = new Size(width, 1)
        };
    }

    // ── Default state ───────────────────────────────────────────────────────────

    [TestMethod]
    public void DefaultState_IsOff()
    {
        Assert.IsFalse(Create().IsOn);
    }

    [TestMethod]
    public void DefaultLabels_OnAndOff()
    {
        var btn = Create();
        Assert.AreEqual("ON",  btn.LabelOn);
        Assert.AreEqual("OFF", btn.LabelOff);
    }

    [TestMethod]
    public void CanFocus_IsTrue()
    {
        Assert.IsTrue(Create().CanFocus);
    }

    // ── Toggle via keyboard ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task Space_TogglesFromOffToOn()
    {
        var btn = Create(isOn: false);

        await btn.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.IsTrue(btn.IsOn);
    }

    [TestMethod]
    public async Task Enter_TogglesFromOnToOff()
    {
        var btn = Create(isOn: true);

        await btn.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        Assert.IsFalse(btn.IsOn);
    }

    [TestMethod]
    public async Task Space_TogglesBack_WhenPressedTwice()
    {
        var btn = Create(isOn: false);

        await btn.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        await btn.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.IsFalse(btn.IsOn);
    }

    [TestMethod]
    public async Task Space_ReturnsHandledTrue()
    {
        var handled = await Create().HandleKeyEventAsync(
            new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.IsTrue(handled);
    }

    // ── Toggle via mouse ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LeftClick_TogglesState()
    {
        var btn = Create(isOn: false);

        await btn.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });

        Assert.IsTrue(btn.IsOn);
    }

    [TestMethod]
    public async Task LeftClick_OutsideBounds_DoesNotToggle()
    {
        var btn = Create(isOn: false);

        await btn.HandleMouseEventAsync(new MouseEvent { Position = new Position(99, 99), LeftClicked = true });

        Assert.IsFalse(btn.IsOn);
    }

    // ── Toggled event ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Toggled_Event_FiredWithNewValue()
    {
        var btn = Create(isOn: false);
        bool? received = null;
        btn.Toggled += (_, v) => received = v;

        await btn.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.AreEqual(true, received);
    }

    [TestMethod]
    public async Task Toggled_Event_FiredEachToggle()
    {
        var btn = Create(isOn: false);
        var calls = new List<bool>();
        btn.Toggled += (_, v) => calls.Add(v);

        await btn.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        await btn.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        CollectionAssert.AreEqual(new[] { true, false }, calls);
    }

    [TestMethod]
    public void SetIsOn_Directly_FiresToggled()
    {
        var btn = Create(isOn: false);
        bool? received = null;
        btn.Toggled += (_, v) => received = v;

        btn.IsOn = true;

        Assert.AreEqual(true, received);
    }

    [TestMethod]
    public void SetIsOn_ToSameValue_DoesNotFireToggled()
    {
        var btn = Create(isOn: true);
        var calls = 0;
        btn.Toggled += (_, _) => calls++;

        btn.IsOn = true; // no change

        Assert.AreEqual(0, calls);
    }

    // ── IsReadOnly ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ReadOnly_SpaceDoesNotToggle()
    {
        var btn = Create(isOn: false);
        btn.IsReadOnly = true;

        await btn.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.IsFalse(btn.IsOn);
    }

    [TestMethod]
    public async Task ReadOnly_ClickDoesNotToggle()
    {
        var btn = Create(isOn: false);
        btn.IsReadOnly = true;

        await btn.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });

        Assert.IsFalse(btn.IsOn);
    }

    // ── Rendering ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Draw_WhenOff_ShowsLabelOff()
    {
        var btn = Create(isOn: false, width: 7);
        var buffer = new MockBuffer(new Size(7, 1));

        btn.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("OFF"));
    }

    [TestMethod]
    public void Draw_WhenOn_ShowsLabelOn()
    {
        var btn = Create(isOn: true, width: 7);
        var buffer = new MockBuffer(new Size(7, 1));

        btn.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("ON"));
    }

    [TestMethod]
    public void Draw_AlwaysHasBrackets()
    {
        var btn = Create(width: 7);
        var buffer = new MockBuffer(new Size(7, 1));

        btn.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("[") && content.Contains("]"));
    }

    [TestMethod]
    public void Draw_CustomLabels_Shown()
    {
        var btn = Create(isOn: true, width: 12);
        btn.LabelOn  = "ENABLED";
        btn.LabelOff = "DISABLED";
        var buffer = new MockBuffer(new Size(12, 1));

        btn.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("ENABLED"));
    }

    [TestMethod]
    public void Draw_Disabled_DoesNotThrow()
    {
        var btn = Create(width: 7);
        btn.IsEnabled = false;
        var buffer = new MockBuffer(new Size(7, 1));

        btn.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    // ── Discoverability via Commands (so Help lists them) ──────────────────────

    [TestMethod]
    public void Commands_IncludeSpaceAndEnterWithNonEmptyKeyDisplayText()
    {
        var btn = Create();

        Assert.IsTrue(btn.Commands.Any(c => c.Key!.ConsoleKey == ConsoleKey.Spacebar
            && !string.IsNullOrEmpty(c.KeyDisplayText)));
        Assert.IsTrue(btn.Commands.Any(c => c.Key!.ConsoleKey == ConsoleKey.Enter
            && !string.IsNullOrEmpty(c.KeyDisplayText)));
    }
}
