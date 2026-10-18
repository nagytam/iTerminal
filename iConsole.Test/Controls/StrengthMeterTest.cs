using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class StrengthMeterTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    [TestMethod]
    [DataRow("", PasswordStrength.None)]
    [DataRow("abc", PasswordStrength.Weak)]
    [DataRow("aaaaaaaaaaaa", PasswordStrength.Weak)]
    [DataRow("password1", PasswordStrength.Weak)]
    [DataRow("abcdefgh", PasswordStrength.Weak)]
    [DataRow("76543210", PasswordStrength.Weak)]
    [DataRow("sunshine", PasswordStrength.Weak)]
    [DataRow("Sunshine7", PasswordStrength.Fair)]
    [DataRow("Sunshine7xyz", PasswordStrength.Good)]
    [DataRow("Xk9!vQ2#mLp7", PasswordStrength.Strong)]
    public void Rate_FollowsLengthMixAndTheObviousVeto(string password, PasswordStrength expected)
    {
        Assert.AreEqual(expected, StrengthMeter.Rate(password));
    }

    [TestMethod]
    public void Render_ShowsFilledStepsAndName()
    {
        Assert.AreEqual("▱▱▱▱", StrengthMeter.Render(PasswordStrength.None));
        Assert.AreEqual("▰▱▱▱ Weak", StrengthMeter.Render(PasswordStrength.Weak));
        Assert.AreEqual("▰▰▰▰ Strong", StrengthMeter.Render(PasswordStrength.Strong));
    }

    [TestMethod]
    public void Draw_ShowsTheRatingOfTheLastPassword()
    {
        MockBuffer buffer = new(new Size(14, 1));
        StrengthMeter meter = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(14, 1) };
        meter.Password = "Xk9!vQ2#mLp7";
        meter.Draw(buffer);
        Assert.AreEqual("▰▰▰▰ Strong", buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd());
    }

    [TestMethod]
    public void Draw_AfterTheTextIsCleared_BlanksTheName()
    {
        MockBuffer buffer = new(new Size(14, 1));
        StrengthMeter meter = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(14, 1) };
        meter.Password = "Xk9!vQ2#mLp7";
        meter.Draw(buffer);
        meter.Password = string.Empty;
        meter.Draw(buffer);
        Assert.AreEqual("▱▱▱▱", buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd());
    }

    private static ListBox<string> CreateListBox(IEnumerable<string> items)
    {
        ConcurrentObservableCollection<string> collection = new(items);
        ListBoxModel<string> model = new(collection);
        model.UpdateListItems();
        ListBox<string> listBox = new(model, Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 4) };
        listBox.OnInitializeLayout();
        return listBox;
    }

    private static KeyEvent Letter(char c) => new(c, (ConsoleKey)char.ToUpperInvariant(c), false, false, false);

    [TestMethod]
    public void FindPrefix_IgnoresMarkersAndWrapsAround()
    {
        string[] values = ["● Default", "  Dark", "  Light"];
        Assert.AreEqual(1, ListBox<string>.FindPrefix(values, "d", 1));
        Assert.AreEqual(0, ListBox<string>.FindPrefix(values, "d", 2));
        Assert.AreEqual(2, ListBox<string>.FindPrefix(values, "li", 0));
        Assert.AreEqual(-1, ListBox<string>.FindPrefix(values, "z", 0));
    }

    [TestMethod]
    public async Task TypeAhead_IsOffByDefault()
    {
        ListBox<string> listBox = CreateListBox(["Alpha", "Beta"]);
        listBox.SetFocus();
        Assert.IsFalse(await listBox.HandleKeyEventAsync(Letter('b')));
        Assert.AreEqual(0, (int)listBox.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task TypeAhead_MovesTheCursorToTheMatchingRow()
    {
        ListBox<string> listBox = CreateListBox(["Alpha", "Beta", "Bravo", "Gamma"]);
        listBox.EnableTypeAhead = true;
        listBox.SetFocus();

        await listBox.HandleKeyEventAsync(Letter('b'));
        Assert.AreEqual(1, (int)listBox.ScrollableModel.CursorPosition.Y);

        // The same letter again steps to the next row with it ...
        await listBox.HandleKeyEventAsync(Letter('b'));
        Assert.AreEqual(2, (int)listBox.ScrollableModel.CursorPosition.Y);

        // ... and with the pause long gone, a different letter starts a new search.
        listBox.TypeAheadTimeout = TimeSpan.Zero;
        await listBox.HandleKeyEventAsync(Letter('g'));
        Assert.AreEqual(3, (int)listBox.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task TypeAhead_NoMatchLeavesTheCursorAndTheKey()
    {
        ListBox<string> listBox = CreateListBox(["Alpha", "Beta"]);
        listBox.EnableTypeAhead = true;
        listBox.SetFocus();
        Assert.IsFalse(await listBox.HandleKeyEventAsync(Letter('z')));
        Assert.AreEqual(0, (int)listBox.ScrollableModel.CursorPosition.Y);
    }
}
