using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class FormPanelTest
{
    private static Mock<IThemeService> ThemeMock()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return themeService;
    }

    [TestMethod]
    public void FieldsShareALeftEdgeAndStackVertically()
    {
        var theme = ThemeMock();
        var form = new FormPanel(theme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 10)
        };
        var first = new CheckBox(theme.Object);
        var second = new CheckBox(theme.Object);

        form.AddField("Name", first);
        form.AddField("Email", second);
        form.OnInitializeLayout();

        // Label column = max("Name"=4, "Email"=5) = 5, plus ": " (2) => fields start at x = 7.
        Assert.AreEqual(7, (int)first.Position.X);
        Assert.AreEqual(7, (int)second.Position.X);

        // Fields stack: row 0, then row 1 (each one cell tall).
        Assert.AreEqual(0, (int)first.Position.Y);
        Assert.AreEqual(1, (int)second.Position.Y);
    }

    [TestMethod]
    public void MultiRowFieldPushesTheNextFieldDown()
    {
        var theme = ThemeMock();
        var form = new FormPanel(theme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 10)
        };
        var group = new RadioGroup<string>(theme.Object) { ViewSize = new Size(20, 3) };
        group.SetItems(["A", "B", "C"]);
        var trailing = new CheckBox(theme.Object);

        form.AddField("Size", group);
        form.AddField("Done", trailing);
        form.OnInitializeLayout();

        Assert.AreEqual(0, (int)group.Position.Y);
        // The three-row radio group occupies rows 0-2, so the next field lands on row 3.
        Assert.AreEqual(3, (int)trailing.Position.Y);
    }

    [TestMethod]
    public void DrawRendersAlignedLabels()
    {
        var theme = ThemeMock();
        var buffer = new MockBuffer(new Size(30, 10));
        var form = new FormPanel(theme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 10)
        };
        form.AddField("Name", new CheckBox(theme.Object));
        form.AddField("Email", new CheckBox(theme.Object));

        form.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("Name"));
        Assert.IsTrue(content.Contains("Email"));
        // Labels are right-aligned to a 5-wide column, so the colons line up at the same column.
        Assert.IsTrue(content.Contains(" Name: "));
        Assert.IsTrue(content.Contains("Email: "));
    }
}
