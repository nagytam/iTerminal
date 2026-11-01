using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iTerminal.Core.Controls.StatusBar;
using iTerminal.Core.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.StatusBar.Test;

/// <summary>
/// The hot key bar at the bottom of the screen. It shows one layer at a time — the plain function
/// keys, or the chords for whichever modifiers are held — and has to fit the screen whatever it shows.
/// </summary>
[TestClass]
public class HotKeysTest
{
    /// <summary>Exposes the modifier state the bar otherwise reads from the physical keyboard.</summary>
    private sealed class TestHotKeys() : HotKeys(
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        NullSettingsService.Instance, null!, TestTheme())
    {
        public void Hold(bool shift = false, bool control = false, bool alt = false)
        {
            IsShiftActive = shift;
            IsControlActive = control;
            IsAltActive = alt;
        }
    }

    private static (TestHotKeys HotKeys, Control Parent) Create(int screenWidth = 400)
    {
        var parent = new Control(TestTheme()) { ViewSize = new Size(screenWidth, 1) };
        var hotKeys = new TestHotKeys();
        parent.AddControl(hotKeys);
        return (hotKeys, parent);
    }

    private static List<Button> Visible(HotKeys hotKeys) =>
        [.. hotKeys.Controls.AsSnapshot().OfType<Button>().Where(b => b.IsVisible)];

    [Timeout(60_000)]
    [TestMethod]
    public void TheBaseLayerShowsThePlainFunctionKeys()
    {
        var (hotKeys, _) = Create();

        hotKeys.LayoutHotKeysRow();

        var titles = Visible(hotKeys).Select(b => b.Title).ToList();
        CollectionAssert.Contains(titles, "F1-Help");
        CollectionAssert.Contains(titles, "F2-Terminal");
        Assert.IsTrue(Visible(hotKeys).All(b => b.Commands.First().Key is { Alt: false, Control: false, Shift: false }));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HoldingAltShowsOnlyTheAltChords()
    {
        var (hotKeys, _) = Create();
        hotKeys.Hold(alt: true);

        hotKeys.LayoutHotKeysRow();

        var visible = Visible(hotKeys);
        Assert.IsTrue(visible.Count > 0, "the Alt layer has keys to show");
        Assert.IsTrue(visible.All(b => b.Commands.First().Key is { Alt: true, Control: false, Shift: false }));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HoldingControlShowsOnlyTheControlChords()
    {
        var (hotKeys, _) = Create();
        hotKeys.Hold(control: true);

        hotKeys.LayoutHotKeysRow();

        Assert.IsTrue(Visible(hotKeys).All(b => b.Commands.First().Key is { Control: true, Alt: false, Shift: false }));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ButtonsArePackedLeftToRightWithoutOverlapping()
    {
        var (hotKeys, _) = Create();

        hotKeys.LayoutHotKeysRow();

        var visible = Visible(hotKeys);
        for (var i = 1; i < visible.Count; i++)
        {
            Assert.AreEqual(visible[i - 1].Position.X + visible[i - 1].ViewSize.Width, visible[i].Position.X);
        }

        Assert.AreEqual(visible.Sum(b => (long)b.ViewSize.Width), hotKeys.ViewSize.Width);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AtNaturalWidthEveryLabelFitsItsButton()
    {
        var (hotKeys, _) = Create(screenWidth: 400);

        hotKeys.LayoutHotKeysRow();

        Assert.IsTrue(Visible(hotKeys).All(b => b.ViewSize.Width == b.Title.Length + 2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnANarrowScreenTheRowIsShrunkToFitLeavingRoomForTheClock()
    {
        var (hotKeys, _) = Create(screenWidth: 60);

        hotKeys.LayoutHotKeysRow();

        Assert.IsTrue(hotKeys.ViewSize.Width <= 56, $"row is {hotKeys.ViewSize.Width} wide");
        Assert.IsTrue(Visible(hotKeys).All(b => b.ViewSize.Width >= 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReleasingTheModifierBringsBackTheBaseLayer()
    {
        var (hotKeys, _) = Create();
        hotKeys.Hold(alt: true);
        hotKeys.LayoutHotKeysRow();

        hotKeys.Hold();
        hotKeys.LayoutHotKeysRow();

        CollectionAssert.Contains(Visible(hotKeys).Select(b => b.Title).ToList(), "F1-Help");
    }

    /// <summary>
    /// The key a button names is the key its command is bound to. The Ctrl layer used to offer
    /// "Ctrl-C-Calc" (the calculator is Ctrl+A; Ctrl+C quits) and the Alt layer "Alt-F6-Tasks" for the
    /// theme manager.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void EveryButtonNamesTheKeyItsCommandIsBoundTo()
    {
        var (hotKeys, _) = Create();
        var mismatches = new List<string>();
        foreach (var button in hotKeys.Controls.AsSnapshot().OfType<Button>())
        {
            var key = button.Commands.FirstOrDefault()?.Key;
            var dash = button.Title.LastIndexOf('-');
            if (key is null || dash < 0)
            {
                continue;
            }

            var named = button.Title[..dash].Split('-').Select(p => p.ToLowerInvariant()).Order().ToList();
            var bound = new List<string> { key.ConsoleKey.ToString().ToLowerInvariant() };
            if (key.Control) bound.Add("ctrl");
            if (key.Alt) bound.Add("alt");
            if (key.Shift) bound.Add("shift");
            if (!named.SequenceEqual(bound.Order()))
            {
                mismatches.Add($"{button.Title} is bound to {iConsole.Commands.Command.FormatKey(key)}");
            }
        }

        Assert.AreEqual(0, mismatches.Count, string.Join(Environment.NewLine, mismatches));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheThemeButtonIsNamedForThemes()
    {
        var (hotKeys, _) = Create();

        CollectionAssert.Contains(hotKeys.Controls.AsSnapshot().OfType<Button>().Select(b => b.Title).ToList(), "Alt-F6-Themes");
    }

    /// <summary>
    /// Too narrow for the full labels, the long words are cut to a common length and the short ones
    /// kept: every button used to be scaled alike — "[F1-Hel][F2-Termi][F3-Vie]".
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void ShorteningCutsTheLongWordsAndKeepsTheShortOnes()
    {
        string[] titles = ["F1-Help", "F2-Terminal", "F3-View", "F12-Screens"];
        var naturals = titles.Select(t => t.Length + 2).ToList();

        var widths = HotKeys.FitTitlesToBudget(titles, naturals, naturals.Sum() - 4);

        CollectionAssert.AreEqual(new[] { 9, 10, 9, 11 }, widths, "[F1-Help][F2-Termi][F3-View][F12-Scree]");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BelowReadableWordsTheExpendableButtonsAreLeftOutFirst()
    {
        string[] titles = ["F1-Help", "F2-Terminal", "F8-Delete", "F9-Tasks", "F10-Quit", "F12-Screens"];
        var naturals = titles.Select(t => t.Length + 2).ToList();

        // With four-letter words the row is 9 + 9 + 9 + 9 + 10 + 10 = 56 wide.
        var widths = HotKeys.FitTitlesToBudget(titles, naturals, 36);

        Assert.AreEqual(0, widths[5], "F12 goes first");
        Assert.AreEqual(0, widths[3], "then F9");
        Assert.AreEqual(0, widths[1], "then F2 — Ctrl+O reaches the terminal too");
        Assert.AreNotEqual(0, widths[2], "Delete stays");
        Assert.AreNotEqual(0, widths[4], "Quit stays");
        Assert.IsTrue(widths.Sum() <= 36);
        Assert.AreEqual(9, widths[0], "the words that remain are readable");
    }

    /// <summary>The first cut of this left F10-Quit out at 80 columns while F7 and F8 stayed.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public void HelpAndQuitAreTheLastToGo()
    {
        string[] titles = ["F1-Help", "F5-Copy", "F6-Rename", "F7-Create", "F10-Quit"];
        var naturals = titles.Select(t => t.Length + 2).ToList();

        var widths = HotKeys.FitTitlesToBudget(titles, naturals, 30);

        Assert.AreNotEqual(0, widths[0]);
        Assert.AreNotEqual(0, widths[4]);
        Assert.AreEqual(0, widths[3], "F7, the rightmost of the rest, gives way instead");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnATinyScreenTheKeysThatAreLeftAreKeptDownToALetter()
    {
        string[] titles = ["F1-Help", "F2-Terminal", "F3-View", "F10-Quit"];
        var naturals = titles.Select(t => t.Length + 2).ToList();

        var widths = HotKeys.FitTitlesToBudget(titles, naturals, 13);

        CollectionAssert.AreEqual(new[] { 6, 0, 0, 7 }, widths, "[F1-H][F10-Q]: half the buttons, Help and Quit kept, never a cut key");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void At100ColumnsTheBaseLayerKeepsReadableWords()
    {
        var (hotKeys, _) = Create(screenWidth: 100);

        hotKeys.LayoutHotKeysRow();

        var visible = Visible(hotKeys);
        Assert.IsTrue(hotKeys.ViewSize.Width <= 96);
        foreach (var button in visible)
        {
            var dash = button.Title.LastIndexOf('-');
            var shownWord = (int)button.ViewSize.Width - 2 - (dash + 1);
            Assert.IsTrue(dash < 0 || shownWord >= Math.Min(4, button.Title.Length - dash - 1), $"{button.Title} shows {shownWord} letters");
        }
        CollectionAssert.IsSubsetOf(new[] { "F1-Help", "F3-View", "F5-Copy", "F6-Rename", "F7-Find", "F8-Delete", "F10-Quit" },
            visible.Select(b => b.Title).ToList());
    }

    /// <summary>
    /// A row too narrow for every button used to show each one and then hide the ones left out, on
    /// every layout. Each change moves the tree version, which repaints the screen from scratch and
    /// lays this row out again — so below about 100 columns the renderer never went idle and nothing
    /// that waits for an idle pass ran (the disk manager said "No drives found" with three drives).
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void LayingOutANarrowRowAgainChangesNoVisibility()
    {
        var (hotKeys, _) = Create(screenWidth: 80);
        hotKeys.LayoutHotKeysRow();
        Assert.IsTrue(hotKeys.Controls.AsSnapshot().OfType<Button>().Any(b => !b.IsVisible && b.Commands.First().Key is { Alt: false, Control: false, Shift: false }),
            "at 80 columns some base-layer buttons are left out");

        var changes = 0;
        foreach (var button in hotKeys.Controls.AsSnapshot().OfType<Button>())
        {
            button.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Button.IsVisible))
                {
                    Interlocked.Increment(ref changes);
                }
            };
        }

        hotKeys.LayoutHotKeysRow();
        hotKeys.LayoutHotKeysRow();

        Assert.AreEqual(0, changes, "an unchanged row must not touch any button's visibility");
    }
}