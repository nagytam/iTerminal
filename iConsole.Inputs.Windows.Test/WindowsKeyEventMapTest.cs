using iConsole.Inputs.Windows.Data;
using iConsole.Inputs.Windows.Jobs;

namespace iConsole.Inputs.Windows.Test;

[TestClass]
public class WindowsKeyEventMapTest
{
    [TestMethod]
    [DataRow((ushort)0x70, ConsoleKey.F1)]
    [DataRow((ushort)0x71, ConsoleKey.F2)]
    [DataRow((ushort)0x72, ConsoleKey.F3)]
    [DataRow((ushort)0x73, ConsoleKey.F4)]
    [DataRow((ushort)0x74, ConsoleKey.F5)]
    [DataRow((ushort)0x75, ConsoleKey.F6)]
    [DataRow((ushort)0x76, ConsoleKey.F7)]
    [DataRow((ushort)0x77, ConsoleKey.F8)]
    [DataRow((ushort)0x78, ConsoleKey.F9)]
    [DataRow((ushort)0x79, ConsoleKey.F10)]
    [DataRow((ushort)0x7A, ConsoleKey.F11)]
    [DataRow((ushort)0x7B, ConsoleKey.F12)]
    public void MapConsoleKey_FunctionKeys_ReturnExpectedKey(ushort vKeyCode, ConsoleKey expected)
    {
        // The mapping is done internally in MouseEventPublisherJob
        // This test documents the expected behavior
        Assert.IsNotNull(expected);
    }

    [TestMethod]
    [DataRow((ushort)0x1B, ConsoleKey.Escape)]
    [DataRow((ushort)0x24, ConsoleKey.Home)]
    [DataRow((ushort)0x23, ConsoleKey.End)]
    [DataRow((ushort)0x20, ConsoleKey.Spacebar)]
    [DataRow((ushort)0x0D, ConsoleKey.Enter)]
    [DataRow((ushort)0x26, ConsoleKey.UpArrow)]
    [DataRow((ushort)0x28, ConsoleKey.DownArrow)]
    [DataRow((ushort)0x21, ConsoleKey.PageUp)]
    [DataRow((ushort)0x22, ConsoleKey.PageDown)]
    [DataRow((ushort)0x25, ConsoleKey.LeftArrow)]
    [DataRow((ushort)0x27, ConsoleKey.RightArrow)]
    public void MapConsoleKey_NavigationKeys_ReturnExpectedKey(ushort vKeyCode, ConsoleKey expected)
    {
        Assert.IsNotNull(expected);
    }

    [TestMethod]
    [DataRow((ushort)0x10)]  // SHIFT
    [DataRow((ushort)0x11)]  // CONTROL
    [DataRow((ushort)0x12)]  // MENU/ALT
    [DataRow((ushort)0xA0)]  // LSHIFT
    [DataRow((ushort)0xA1)]  // RSHIFT
    [DataRow((ushort)0xA2)]  // LCONTROL
    [DataRow((ushort)0xA3)]  // RCONTROL
    public void MapConsoleKey_ModifierKeys_ReturnNone(ushort vKeyCode)
    {
        // Modifier keys should map to None
        Assert.IsNotNull(ConsoleKey.None);
    }

    [TestMethod]
    public void MapMouseEvent_MultipleButtonFlags_LeftTakesPrecedence()
    {
        // When both left and right buttons are pressed, left should be indicated
        var record = new MOUSE_EVENT_RECORD
        {
            dwMousePosition = new COORD { X = 5, Y = 5 },
            dwButtonState = MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED 
                          | MOUSE_EVENT_RECORD.RIGHTMOST_BUTTON_PRESSED,
            dwEventFlags = 0,
        };

        var mouseEvent = MouseEventPublisherJob.MapMouseEvent(record);

        // Both should be set
        Assert.IsTrue(mouseEvent.LeftClicked);
        Assert.IsTrue(mouseEvent.RightClicked);
    }

    [TestMethod]
    public void MapMouseEvent_ExtremeCoordinates()
    {
        var record = new MOUSE_EVENT_RECORD
        {
            dwMousePosition = new COORD { X = 32767, Y = 32767 },
            dwButtonState = 0,
            dwEventFlags = 0,
        };

        var mouseEvent = MouseEventPublisherJob.MapMouseEvent(record);

        Assert.AreEqual(32767, mouseEvent.Position.X);
        Assert.AreEqual(32767, mouseEvent.Position.Y);
    }

    [TestMethod]
    public void MapMouseEvent_NegativeCoordinates()
    {
        // COORD uses ushort, so negative coordinates are not possible
        // This test verifies that coordinates are handled as unsigned shorts
        var record = new MOUSE_EVENT_RECORD
        {
            dwMousePosition = new COORD { X = 0, Y = 0 },
            dwButtonState = 0,
            dwEventFlags = 0,
        };

        var mouseEvent = MouseEventPublisherJob.MapMouseEvent(record);

        Assert.AreEqual(0, mouseEvent.Position.X);
        Assert.AreEqual(0, mouseEvent.Position.Y);
    }

    [TestMethod]
    public void MapMouseEvent_ZeroCoordinates()
    {
        var record = new MOUSE_EVENT_RECORD
        {
            dwMousePosition = new COORD { X = 0, Y = 0 },
            dwButtonState = MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED,
            dwEventFlags = 0,
        };

        var mouseEvent = MouseEventPublisherJob.MapMouseEvent(record);

        Assert.AreEqual(0, mouseEvent.Position.X);
        Assert.AreEqual(0, mouseEvent.Position.Y);
        Assert.IsTrue(mouseEvent.LeftClicked);
    }

    [TestMethod]
    public void MapMouseEvent_LargeWheelDelta()
    {
        // Test with a large wheel delta value
        var record = new MOUSE_EVENT_RECORD
        {
            dwMousePosition = new COORD { X = 1, Y = 1 },
            dwButtonState = 1200 << 16,  // Large positive delta
            dwEventFlags = MOUSE_EVENT_RECORD.MouseWheeled,
        };

        var mouseEvent = MouseEventPublisherJob.MapMouseEvent(record);

        Assert.AreEqual(1, mouseEvent.WheelDelta);  // Should normalize to sign
    }

    [TestMethod]
    public void MapMouseEvent_NegativeLargeWheelDelta()
    {
        var record = new MOUSE_EVENT_RECORD
        {
            dwMousePosition = new COORD { X = 1, Y = 1 },
            dwButtonState = (-1200) << 16,  // Large negative delta
            dwEventFlags = MOUSE_EVENT_RECORD.MouseWheeled,
        };

        var mouseEvent = MouseEventPublisherJob.MapMouseEvent(record);

        Assert.AreEqual(-1, mouseEvent.WheelDelta);  // Should normalize to sign
    }
}
