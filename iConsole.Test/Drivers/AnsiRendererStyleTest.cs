using iConsole.Display;
using iConsole.Drivers;

namespace iConsole.Test.Drivers;

/// <summary>The text attributes and OSC 8 hyperlinks <see cref="AnsiRenderer"/> writes. It changes a static switch, so it runs alone.</summary>
[TestClass]
[DoNotParallelize]
public class AnsiRendererStyleTest
{
    private const string Esc = "\x1b";
    private bool _hyperlinks;

    [TestInitialize]
    public void Setup()
    {
        _hyperlinks = AnsiRenderer.HyperlinksEnabled;
        AnsiRenderer.HyperlinksEnabled = true;
    }

    [TestCleanup]
    public void Cleanup() => AnsiRenderer.HyperlinksEnabled = _hyperlinks;

    [TestMethod]
    public void DimBlinkReverseAndOverlineAreWrittenAsTheirOwnCodes()
    {
        Conxel conxel = new('a', Color.DarkYellow, Color.Blue) { IsDim = true, IsBlink = true, IsReverse = true, IsOverline = true };

        string ansi = AnsiRenderer.Conxel2Ansi(0, 0, conxel).ToString();

        Assert.AreEqual($"{Esc}[1;1H{Esc}[33;104;2;5;7;53ma{Esc}[0m", ansi);
    }

    [TestMethod]
    public void UnderlineStyleAndColourAreSubParameters()
    {
        Conxel conxel = new('a', Color.DarkYellow, Color.Blue, isUnderline: true)
        {
            UnderlineStyle = UnderlineStyle.Curly,
            HasUnderlineColor = true,
            UnderlineColor = Color.Red,
        };

        string ansi = AnsiRenderer.Conxel2Ansi(0, 0, conxel).ToString();

        Assert.AreEqual($"{Esc}[1;1H{Esc}[33;104;4:3;58:5:9ma{Esc}[0m", ansi);
    }

    [TestMethod]
    public void APlainUnderlineKeepsTheShortForm()
    {
        Conxel conxel = new('a', Color.DarkYellow, Color.Blue, isUnderline: true);

        Assert.AreEqual($"{Esc}[1;1H{Esc}[33;104;4ma{Esc}[0m", AnsiRenderer.Conxel2Ansi(0, 0, conxel).ToString());
    }

    [TestMethod]
    public void ALinkIsOpenedBeforeItsTextAndClosedAfter()
    {
        ushort id = Hyperlinks.Register("https://example.com/x");
        Conxel conxel = new('a', Color.DarkYellow, Color.Blue) { LinkId = id };

        string ansi = AnsiRenderer.Conxel2Ansi(0, 0, conxel).ToString();

        Assert.AreEqual(
            $"{Esc}[1;1H{Esc}[33;104m{Esc}]8;id=iT{id};https://example.com/x{Esc}\\a{Esc}]8;;{Esc}\\{Esc}[0m", ansi);
    }

    [TestMethod]
    public void ALinkSpanningSeveralCellsIsOpenedOnce()
    {
        ConxelString text = new("abc", Color.White, Color.Black);
        text.SetHyperlink("https://example.com/span", 1, 3);
        ushort id = Hyperlinks.Register("https://example.com/span");

        string ansi = AnsiRenderer.ConxelString2Ansi(0, 0, text);

        Assert.AreEqual(1, CountOf(ansi, $"{Esc}]8;id=iT{id};"));
        Assert.IsTrue(ansi.Contains($"a{Esc}]8;id=iT{id};https://example.com/span{Esc}\\bc{Esc}]8;;{Esc}\\", StringComparison.Ordinal), ansi);
    }

    [TestMethod]
    public void ALinkIsClosedWhenTheStringEnds()
    {
        ConxelString text = new("ab", Color.White, Color.Black);
        text.SetHyperlink("https://example.com/end");

        string ansi = AnsiRenderer.ConxelString2Ansi(0, 0, text);

        Assert.AreEqual(1, CountOf(ansi, $"{Esc}]8;;{Esc}\\"));
        Assert.IsTrue(ansi.IndexOf($"{Esc}]8;;", StringComparison.Ordinal) < ansi.LastIndexOf($"{Esc}[0m", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NoLinkIsWrittenWhenHyperlinksAreSwitchedOff()
    {
        AnsiRenderer.HyperlinksEnabled = false;
        ConxelString text = new("ab", Color.White, Color.Black);
        text.SetHyperlink("https://example.com/off");

        string ansi = AnsiRenderer.ConxelString2Ansi(0, 0, text);

        Assert.IsFalse(ansi.Contains($"{Esc}]8", StringComparison.Ordinal));
        Assert.IsFalse(AnsiRenderer.NeedsAnsi(text[0]));
    }

    [TestMethod]
    public void AnAttributeTurnedOffIsClearedBeforeTheNextCell()
    {
        // The sequence only adds attributes, so without the reset the second cell would stay bold.
        ConxelString text = new();
        text.Append('a', Color.DarkYellow, Color.Blue, isBold: true);
        text.Append('b', Color.DarkYellow, Color.Blue);

        string ansi = AnsiRenderer.ConxelString2Ansi(0, 0, text);

        Assert.AreEqual($"{Esc}[1;1H{Esc}[33;104;1ma{Esc}[0;33;104mb{Esc}[0m", ansi);
    }

    [TestMethod]
    public void NeedsAnsiIsTrueOnlyForCellsThePlainConsoleCannotDraw()
    {
        Assert.IsFalse(AnsiRenderer.NeedsAnsi(new Conxel('a', Color.White, Color.Black)));
        Assert.IsTrue(AnsiRenderer.NeedsAnsi(new Conxel('a', Color.White, Color.Black) { IsDim = true }));
        Assert.IsTrue(AnsiRenderer.NeedsAnsi(new Conxel('a', Color.White, Color.Black, isBold: true)));
        Assert.IsTrue(AnsiRenderer.NeedsAnsi(new Conxel('a', Color.White, Color.Black) { LinkId = Hyperlinks.Register("https://example.com/n") }));
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
