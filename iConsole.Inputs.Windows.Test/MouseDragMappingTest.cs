using iConsole.Inputs.Windows.Data;
using iConsole.Inputs.Windows.Jobs;

namespace iConsole.Inputs.Windows.Test;

/// <summary>A move with the left button down is a drag; a move with no button, a press and the wheel are not.</summary>
[TestClass]
public class MouseDragMappingTest
{
    private static MOUSE_EVENT_RECORD Record(int buttons, int flags) => new()
    {
        dwMousePosition = new COORD { X = 7, Y = 3 },
        dwButtonState = buttons,
        dwEventFlags = flags,
    };

    [TestMethod]
    public void MovingWithTheLeftButtonDown_IsADrag_NotAClick()
    {
        var mouseEvent = MouseEventPublisherJob.MapMouseEvent(Record(MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED, MOUSE_EVENT_RECORD.MouseMoved));
        Assert.IsTrue(mouseEvent.IsDrag);
        Assert.IsFalse(mouseEvent.LeftClicked);
        Assert.IsFalse(mouseEvent.IsRelease);
        Assert.AreEqual(7, mouseEvent.Position.X);
        Assert.AreEqual(3, mouseEvent.Position.Y);
    }

    [TestMethod]
    public void MovingWithNoButtonOrTheRightButton_IsNotADrag()
    {
        Assert.IsFalse(MouseEventPublisherJob.MapMouseEvent(Record(0, MOUSE_EVENT_RECORD.MouseMoved)).IsDrag);
        Assert.IsFalse(MouseEventPublisherJob.MapMouseEvent(Record(MOUSE_EVENT_RECORD.RIGHTMOST_BUTTON_PRESSED, MOUSE_EVENT_RECORD.MouseMoved)).IsDrag);
    }

    [TestMethod]
    public void APressAReleaseAndTheWheel_AreNotDrags()
    {
        Assert.IsFalse(MouseEventPublisherJob.MapMouseEvent(Record(MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED, 0)).IsDrag);
        Assert.IsFalse(MouseEventPublisherJob.MapMouseEvent(Record(0, 0)).IsDrag);
        Assert.IsFalse(MouseEventPublisherJob.MapMouseEvent(Record(MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED, MOUSE_EVENT_RECORD.MouseWheeled)).IsDrag);
    }
}
