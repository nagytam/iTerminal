using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class ToastTest
{
    private readonly TestThemeService _theme = new();
    private ColorScheme Scheme => _theme.CurrentColorScheme;

    private static Notification CreateNotification(
        string title,
        string? message = null,
        NotificationSeverity severity = NotificationSeverity.Info) =>
        new(1, title, message, severity, DateTime.Now, TimeSpan.FromSeconds(4));

    private Toast CreateToast(Notification notification, int width = 30)
    {
        return new Toast(notification, _theme)
        {
            ViewSize = new Size(width, Toast.MeasureHeight(notification, width)),
        };
    }

    [TestMethod]
    public void Draw_TitleOnly_IsOneRow_WithBarAndSeverityMark()
    {
        var toast = CreateToast(CreateNotification("Copy finished", severity: NotificationSeverity.Success));

        var screen = ScreenBuffer.Render(toast, 30, 3);

        screen.AssertLines("▌ ✓ Copy finished");
    }

    [TestMethod]
    public void Draw_Message_IsWrappedUnderTheTitle()
    {
        // 30 columns leave 25 for text.
        var toast = CreateToast(CreateNotification("Copy finished", "readme.txt to the target folder on the right"));

        var screen = ScreenBuffer.Render(toast, 30, 5);

        screen.AssertLines(
            "▌ i Copy finished",
            "▌   readme.txt to the target",
            "▌   folder on the right");
    }

    [TestMethod]
    public void Draw_LongMessage_IsCutAtThreeLines_WithAnEllipsis()
    {
        var notification = CreateNotification("Failed", string.Join(" ", Enumerable.Repeat("word", 40)));
        var toast = CreateToast(notification);

        var screen = ScreenBuffer.Render(toast, 30, 8);

        Assert.AreEqual(1 + Toast.MaxMessageLines, Toast.MeasureHeight(notification, 30));
        Assert.IsTrue(screen.Lines[3].EndsWith('…'), screen.ToString());
        Assert.AreEqual(string.Empty, screen.Lines[4]);
    }

    [TestMethod]
    public void Draw_LongTitle_IsCutToTheToast_WithAnEllipsis()
    {
        var toast = CreateToast(CreateNotification("A title far too long to fit in here"), width: 20);

        var screen = ScreenBuffer.Render(toast, 40, 1);

        Assert.AreEqual("▌ i A title far to…", screen.Lines[0]);
    }

    [TestMethod]
    public void Draw_SeverityColoursTheBarAndTheMark_NotTheText()
    {
        (NotificationSeverity Severity, char Mark, Color Accent)[] cases =
        [
            (NotificationSeverity.Info, 'i', Color.Cyan),
            (NotificationSeverity.Success, '✓', Color.Green),
            (NotificationSeverity.Warning, '!', Color.Yellow),
            (NotificationSeverity.Error, '✗', Scheme.Error),
        ];

        foreach (var (severity, mark, accent) in cases)
        {
            var toast = CreateToast(CreateNotification("Title", "Detail", severity));

            var screen = ScreenBuffer.Render(toast, 30, 2);

            screen.AssertColors("▌", accent, Scheme.ButtonBackground);
            screen.AssertColors(mark.ToString(), accent, Scheme.ButtonBackground);
            screen.AssertColors("Title", Scheme.Foreground, Scheme.ButtonBackground);
            screen.AssertColors("Detail", Scheme.Foreground, Scheme.ButtonBackground);
        }
    }

    [TestMethod]
    public void Draw_PaintsEveryCellOfItsRectangle()
    {
        var toast = CreateToast(CreateNotification("Done", "Short"));
        toast.Position = new Position(3, 1);

        var screen = ScreenBuffer.Render(toast, 40, 5);

        screen.AssertFillsBounds(toast);
        Assert.IsFalse(screen.IsWritten(2, 1), "left of the toast");
        Assert.IsFalse(screen.IsWritten(33, 1), "right of the toast");
        Assert.IsFalse(screen.IsWritten(3, 3), "below the toast");
    }

    [TestMethod]
    public async Task Click_InsideTheToast_RaisesClicked_AndOutsideDoesNot()
    {
        var toast = CreateToast(CreateNotification("Done", "Detail"));
        toast.Position = new Position(10, 2);
        var clicks = 0;
        toast.Clicked += (_, _) => clicks++;

        var inside = await toast.HandleMouseEventAsync(new MouseEvent { Position = new Position(39, 3), LeftClicked = true });
        var below = await toast.HandleMouseEventAsync(new MouseEvent { Position = new Position(12, 4), LeftClicked = true });
        var right = await toast.HandleMouseEventAsync(new MouseEvent { Position = new Position(40, 2), LeftClicked = true });
        var moved = await toast.HandleMouseEventAsync(new MouseEvent { Position = new Position(12, 2) });

        Assert.IsTrue(inside);
        Assert.IsFalse(below, "the row under the toast is not the toast");
        Assert.IsFalse(right, "the column after the toast is not the toast");
        Assert.IsFalse(moved, "moving the pointer over a toast is not a click");
        Assert.AreEqual(1, clicks);
    }

    [TestMethod]
    public void Toast_NeverTakesFocus()
    {
        Assert.IsFalse(CreateToast(CreateNotification("Done")).CanFocus);
    }
}
