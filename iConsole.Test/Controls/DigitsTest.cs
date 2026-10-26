using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class DigitsTest
{
    private readonly TestThemeService _theme = new();

    [TestMethod]
    public void ALargeZeroIsFiveRowsOfDoubleWidthBlocks()
    {
        IReadOnlyList<string> rows = Digits.Render("0", DigitsStyle.Large);

        CollectionAssert.AreEqual(new[] { "██████", "██  ██", "██  ██", "██  ██", "██████" }, rows.ToArray());
    }

    [TestMethod]
    public void ACompactOneUsesHalfBlocks()
    {
        IReadOnlyList<string> rows = Digits.Render("1", DigitsStyle.Compact);

        CollectionAssert.AreEqual(new[] { "▄█ ", " █ ", "▀▀▀" }, rows.ToArray());
    }

    [TestMethod]
    public void CharactersAreSeparatedByOneBlankDotColumn()
    {
        Assert.AreEqual(6 + 2 + 6, Digits.Measure("12", DigitsStyle.Large));
        Assert.AreEqual(3 + 1 + 3, Digits.Measure("12", DigitsStyle.Compact));
        Assert.AreEqual(0, Digits.Measure(string.Empty, DigitsStyle.Large));
    }

    [TestMethod]
    public void AColonIsNarrow()
    {
        Assert.AreEqual(2, Digits.Measure(":", DigitsStyle.Large));
        Assert.AreEqual(6 + 2 + 2 + 2 + 6, Digits.Measure("1:2", DigitsStyle.Large));
        CollectionAssert.AreEqual(new[] { "  ", "██", "  ", "██", "  " }, Digits.Render(":", DigitsStyle.Large).ToArray());
    }

    [TestMethod]
    public void EveryRowOfARenderIsAsWideAsTheMeasure()
    {
        foreach (DigitsStyle style in new[] { DigitsStyle.Large, DigitsStyle.Compact })
        {
            IReadOnlyList<string> rows = Digits.Render("12:34.56-+/%ABCDEF", style);

            Assert.AreEqual(Digits.HeightOf(style), rows.Count);
            Assert.IsTrue(rows.All(row => row.Length == Digits.Measure("12:34.56-+/%ABCDEF", style)), style.ToString());
        }
    }

    [TestMethod]
    public void LowercaseLettersUseTheCapitalGlyphs()
    {
        CollectionAssert.AreEqual(Digits.Render("ABCDEF", DigitsStyle.Large).ToArray(), Digits.Render("abcdef", DigitsStyle.Large).ToArray());
    }

    [TestMethod]
    public void ACharacterTheFontLacksIsLeftBlank()
    {
        IReadOnlyList<string> rows = Digits.Render("Z", DigitsStyle.Large);

        Assert.IsTrue(rows.All(row => row.Trim().Length == 0));
        Assert.AreEqual(Digits.Measure(" ", DigitsStyle.Large), rows[0].Length);
    }

    [TestMethod]
    public void EveryDigitIsDistinct()
    {
        List<string> glyphs = [.. "0123456789".Select(c => string.Join("/", Digits.Render(c.ToString(), DigitsStyle.Large)))];

        Assert.AreEqual(10, glyphs.Distinct().Count());
    }

    [TestMethod]
    public void DrawPaintsTheTextAndBlanksTheRestOfTheRectangle()
    {
        Digits digits = new(_theme) { Text = "8", Position = new Position(1, 1), ViewSize = new Size(12, 7) };
        ScreenBuffer buffer = new(20, 10);

        digits.Draw(buffer);

        Assert.AreEqual("██████", buffer.Text(1, 1, 6));
        Assert.AreEqual("██  ██", buffer.Text(1, 2, 6));
        Assert.AreEqual(string.Empty, buffer.Text(1, 6, 12).Trim(), "below the digits");
        Assert.AreEqual(string.Empty, buffer.Text(7, 1, 6).Trim(), "right of the digits");
    }

    [TestMethod]
    public void AChangedTextLeavesNothingOfTheOldBehind()
    {
        Digits digits = new(_theme) { Text = "88", Position = new Position(0, 0), ViewSize = new Size(20, 5) };
        ScreenBuffer buffer = new(20, 5);
        digits.Draw(buffer);

        digits.Text = "1";
        digits.Draw(buffer);

        Assert.AreEqual(string.Empty, buffer.Text(8, 0, 12).Trim());
    }

    [TestMethod]
    public void TheTextCanBeAlignedRightOrCentred()
    {
        Digits digits = new(_theme) { Text = "0", Position = new Position(0, 0), ViewSize = new Size(16, 5), Alignment = DigitsAlignment.Right };
        ScreenBuffer buffer = new(16, 5);

        digits.Draw(buffer);
        Assert.AreEqual("██████", buffer.Text(10, 0, 6));
        Assert.AreEqual(string.Empty, buffer.Text(0, 0, 10).Trim());

        digits.Alignment = DigitsAlignment.Center;
        digits.Draw(buffer);
        Assert.AreEqual("██████", buffer.Text(5, 0, 6));
    }

    [TestMethod]
    public void TextWiderThanTheControlIsCutAtItsEdge()
    {
        Digits digits = new(_theme) { Text = "00", Position = new Position(0, 0), ViewSize = new Size(8, 5) };
        ScreenBuffer buffer = new(20, 5);

        digits.Draw(buffer);

        Assert.AreEqual("██████  ", buffer.Text(0, 0, 8));
        Assert.AreEqual(string.Empty, buffer.Text(8, 0, 12).Trim(), "nothing spills past the rectangle");
    }

    [TestMethod]
    public void TheColoursCanBeSetAndOtherwiseComeFromTheTheme()
    {
        Digits digits = new(_theme) { Text = "1", Position = new Position(0, 0), ViewSize = new Size(10, 5) };
        ScreenBuffer buffer = new(10, 5);
        digits.Draw(buffer);
        Color themed = buffer.ForegroundAt(0, 0);

        digits.ForegroundColor = Color.Red;
        digits.BackgroundColor = Color.Blue;
        digits.Draw(buffer);

        Assert.AreEqual(Color.Red, buffer.ForegroundAt(0, 0));
        Assert.AreEqual(Color.Blue, buffer.BackgroundAt(0, 0));
        Assert.AreNotEqual(Color.Red, themed);
    }

    [TestMethod]
    public void MeasuredSizeIsWhatTheTextNeeds()
    {
        Digits digits = new(_theme) { Text = "12:00" };

        Assert.AreEqual(Digits.Measure("12:00", DigitsStyle.Large), digits.MeasuredSize.Width);
        Assert.AreEqual(5, digits.MeasuredSize.Height);

        digits.Style = DigitsStyle.Compact;
        Assert.AreEqual(3, digits.MeasuredSize.Height);
    }
}
