using iConsole.Controls;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Public helpers that threw for an empty list, a negative count, a null text or a date past the last one.</summary>
[TestClass]
public class ControlsReviewBatch27Test
{
    private readonly TestThemeService _theme = new();

    [Timeout(60_000)]
    [TestMethod]
    public void Skeleton_BuildLine_OfNoLength_IsEmpty()
    {
        Assert.AreEqual(string.Empty, Skeleton.BuildLine(0, 3));
        Assert.AreEqual(string.Empty, Skeleton.BuildLine(-4, 3));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorPicker_Move_OnAnEmptyPaletteStaysAtZero()
    {
        Assert.AreEqual(0, ColorPicker.Move(0, 1, 1, 0));
        Assert.AreEqual(0, ColorPicker.Move(-5, 0, 0, 3));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListBox_FindPrefix_FromBeforeTheFirstRow_Wraps()
    {
        Assert.AreEqual(1, ListBox<string>.FindPrefix(["alpha", "beta", "gamma"], "b", -7));
        Assert.AreEqual(-1, ListBox<string>.FindPrefix([], "b", 0));
        Assert.AreEqual(0, ListBox<string>.FindPrefix(["alpha"], null!, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Countdown_StartingFartherThanTheLastDate_CountsDownToIt()
    {
        Mock<IDateTimeProviderService> clock = new();
        clock.SetupGet(c => c.Now).Returns(new DateTime(2026, 10, 26, 12, 0, 0));
        Countdown countdown = new(_theme, clock.Object);

        countdown.Start(TimeSpan.MaxValue);

        Assert.AreEqual(DateTime.MaxValue, countdown.Target);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StrengthMeter_Render_OfAnUnknownStrength_DoesNotThrow()
    {
        Assert.AreEqual("▰▰▰▰", StrengthMeter.Render((PasswordStrength)9)[..4]);
        Assert.AreEqual("▱▱▱▱", StrengthMeter.Render((PasswordStrength)(-3))[..4]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TimePicker_Wrap_SurvivesAHugeDeltaAndAnEmptyRange()
    {
        Assert.AreEqual(0, TimePicker.Wrap(5, 3, -1));
        Assert.AreEqual(6, TimePicker.Wrap(59, int.MaxValue, 59));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AlignCenter_WithNullText_ReturnsAnEmptyLine()
    {
        Assert.AreEqual(string.Empty, StringHelper.AlignCenter(null, null));
        Assert.AreEqual("abc", StringHelper.AlignCenter("abc", null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TreeView_FormatRow_WithANegativeDepth_HasNoIndent()
    {
        Mock<IHierarchicalItemModel> item = new();
        item.SetupGet(i => i.Name).Returns("leaf");
        item.SetupGet(i => i.Children).Returns([]);

        Assert.AreEqual("  leaf", TreeView.FormatRow(item.Object, -2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HexViewer_TryParseOffset_OfNull_IsFalse()
    {
        Assert.IsFalse(HexViewer.TryParseOffset(null, out long offset));
        Assert.AreEqual(0, offset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_WordKeys_OnNullText_StayAtZero()
    {
        Assert.AreEqual(0, TextBox.WordStart(null, 3));
        Assert.AreEqual(0, TextBox.WordEnd(null, 3));
    }
}
