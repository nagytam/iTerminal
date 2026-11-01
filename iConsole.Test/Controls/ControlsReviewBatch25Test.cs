using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>Fixes from a review of hints that did not match the keys, and of settings that failed or drew off screen when set below zero.</summary>
[TestClass]
public class ControlsReviewBatch25Test
{
    private readonly TestThemeService _theme = new();

    private static string[] Keys(IEnumerable<(string Key, string Description)> hints) => [.. hints.Select(h => h.Key)];

    [Timeout(60_000)]
    [TestMethod]
    public void CheckList_KeyHints_NameEachKeyOnce()
    {
        CheckList list = new(_theme);
        list.SetItems(["a", "b"]);

        string[] keys = Keys(list.GetKeyHints());

        Assert.AreEqual(keys.Length, keys.Distinct().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RadioGroup_KeyHints_DoNotOfferSpace_WhichTheGroupDoesNotTake()
    {
        RadioGroup<string> group = new(_theme);
        group.SetItems(["a", "b"]);

        CollectionAssert.DoesNotContain(Keys(group.GetKeyHints()), "Space");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RadioGroup_ChangingTheOrientation_LaysTheOptionsOutAtOnce()
    {
        RadioGroup<string> group = new(_theme) { ViewSize = new Size(30, 3) };
        group.SetItems(["one", "two"]);
        Assert.AreEqual(1, (int)group.Controls[1].Position.Y);

        group.Orientation = Orientation.Horizontal;

        Assert.AreEqual(0, (int)group.Controls[1].Position.Y);
        Assert.IsTrue(group.Controls[1].Position.X > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyValueList_NegativeGapAndKeyWidth_AreRaisedToZero_AndStillDraw()
    {
        KeyValueList list = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(20, 2), Gap = -3, MaxKeyWidth = -5 };
        list.SetItems([("Name", "iTerminal")]);

        Assert.AreEqual(0, list.Gap);
        Assert.AreEqual(0, list.MaxKeyWidth);
        ScreenBuffer.Render(list, 20, 2);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ComboBox_KeyHints_NameThePageKeysAndTypeAhead()
    {
        ComboBox<string> combo = new(_theme);

        CollectionAssert.IsSubsetOf(new[] { "PageUp/PageDown", "Letters" }, Keys(combo.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ComboBox_ReadOnly_ListsNoKeys()
    {
        ComboBox<string> combo = new(_theme) { IsReadOnly = true };

        Assert.AreEqual(0, combo.GetKeyHints().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToastHost_NegativeOffsets_AreRaisedToZero()
    {
        ToastHost host = new(new NotificationService(), _theme) { TopOffset = -4, RightMargin = -2 };

        Assert.AreEqual(0, host.TopOffset);
        Assert.AreEqual(0, host.RightMargin);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FormPanel_NegativeRowGap_IsRaisedToZero()
    {
        FormPanel panel = new(_theme) { RowGap = -2 };

        Assert.AreEqual(0, panel.RowGap);
    }
}
