using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class LinkLabelHyperlinkTest
{
    private readonly TestThemeService _theme = new();

    [TestMethod]
    public void ALinkWithAnAbsoluteTargetIsDrawnAsAHyperlink()
    {
        LinkLabel link = new(_theme) { Text = "docs", Target = "https://example.com/docs", Position = new Position(0, 0) };
        ScreenBuffer buffer = new(20, 3);

        link.Draw(buffer);

        ushort id = Hyperlinks.Register("https://example.com/docs");
        Assert.AreEqual(id, buffer.Cell(0, 0).LinkId);
        Assert.AreEqual(id, buffer.Cell(3, 0).LinkId);
    }

    [TestMethod]
    public void ATargetThatIsNotAnAbsoluteUriIsOnlyForTheHandler()
    {
        LinkLabel link = new(_theme) { Text = "docs", Target = "help:keys", Position = new Position(0, 0) };
        ScreenBuffer buffer = new(20, 3);

        link.Draw(buffer);

        Assert.AreEqual(Hyperlinks.None, buffer.Cell(0, 0).LinkId);
    }

    [TestMethod]
    public void AScriptTargetIsNeverWrittenAsALink()
    {
        LinkLabel link = new(_theme) { Text = "x", Target = "javascript:alert(1)", Position = new Position(0, 0) };
        ScreenBuffer buffer = new(20, 3);

        link.Draw(buffer);

        Assert.AreEqual(Hyperlinks.None, buffer.Cell(0, 0).LinkId);
    }

    [TestMethod]
    public void ALabelCarriesItsExtraAttributesAndLinkOnTheTextOnly()
    {
        Label label = new("abc", _theme)
        {
            IsDim = true,
            IsUnderline = true,
            UnderlineStyle = UnderlineStyle.Curly,
            UnderlineColor = Color.Red,
            Hyperlink = "https://example.com/label",
            Position = new Position(0, 0),
            ViewSize = new Size(6, 1),
        };
        ScreenBuffer buffer = new(20, 3);

        label.Draw(buffer);

        Conxel first = buffer.Cell(0, 0);
        Assert.IsTrue(first.IsDim);
        Assert.AreEqual(UnderlineStyle.Curly, first.UnderlineStyle);
        Assert.IsTrue(first.HasUnderlineColor);
        Assert.AreEqual(Color.Red, first.UnderlineColor);
        Assert.AreEqual(Hyperlinks.Register("https://example.com/label"), first.LinkId);
        Assert.AreEqual(Hyperlinks.None, buffer.Cell(4, 0).LinkId, "the padding after the text is not part of the link");
    }
}
