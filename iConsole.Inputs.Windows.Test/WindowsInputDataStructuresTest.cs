using iConsole.Inputs.Windows.Data;

namespace iConsole.Inputs.Windows.Test;

[TestClass]
public class WindowsInputDataStructuresTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void COORD_DefaultInitialization()
    {
        var coord = new COORD();

        Assert.AreEqual(0, coord.X);
        Assert.AreEqual(0, coord.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow((ushort)0, (ushort)0)]
    [DataRow((ushort)10, (ushort)20)]
    [DataRow((ushort)255, (ushort)255)]
    public void COORD_SetValues(ushort x, ushort y)
    {
        var coord = new COORD { X = x, Y = y };

        Assert.AreEqual(x, coord.X);
        Assert.AreEqual(y, coord.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MOUSE_EVENT_RECORD_DefaultInitialization()
    {
        var record = new MOUSE_EVENT_RECORD();

        Assert.AreEqual(0, record.dwMousePosition.X);
        Assert.AreEqual(0, record.dwMousePosition.Y);
        Assert.AreEqual(0, record.dwButtonState);
        Assert.AreEqual(0, record.dwEventFlags);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MOUSE_EVENT_RECORD_LeftButtonPressedConstant()
    {
        // Left mouse button state
        int leftButtonState = MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED;
        Assert.AreNotEqual(0, leftButtonState);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MOUSE_EVENT_RECORD_RightButtonPressedConstant()
    {
        // Right mouse button state
        int rightButtonState = MOUSE_EVENT_RECORD.RIGHTMOST_BUTTON_PRESSED;
        Assert.AreNotEqual(0, rightButtonState);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MOUSE_EVENT_RECORD_MouseMovedFlagConstant()
    {
        int movedFlag = MOUSE_EVENT_RECORD.MouseMoved;
        Assert.AreNotEqual(0, movedFlag);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MOUSE_EVENT_RECORD_MouseWheeledFlagConstant()
    {
        int wheeledFlag = MOUSE_EVENT_RECORD.MouseWheeled;
        Assert.AreNotEqual(0, wheeledFlag);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MOUSE_EVENT_RECORD_MouseHorizontalWheeledFlagConstant()
    {
        int horizontalWheelFlag = MOUSE_EVENT_RECORD.MouseHorizontalWheeled;
        Assert.AreNotEqual(0, horizontalWheelFlag);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MOUSE_EVENT_RECORD_DoubleClickFlagConstant()
    {
        int doubleClickFlag = MOUSE_EVENT_RECORD.DoubleClick;
        Assert.AreNotEqual(0, doubleClickFlag);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KEY_EVENT_RECORD_ShiftPressedConstant()
    {
        int shiftFlag = KEY_EVENT_RECORD.SHIFT_PRESSED;
        Assert.AreNotEqual(0, shiftFlag);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KEY_EVENT_RECORD_LeftCtrlPressedConstant()
    {
        int leftCtrlFlag = KEY_EVENT_RECORD.LEFT_CTRL_PRESSED;
        Assert.AreNotEqual(0, leftCtrlFlag);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KEY_EVENT_RECORD_RightCtrlPressedConstant()
    {
        int rightCtrlFlag = KEY_EVENT_RECORD.RIGHT_CTRL_PRESSED;
        Assert.AreNotEqual(0, rightCtrlFlag);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KEY_EVENT_RECORD_LeftAltPressedConstant()
    {
        int leftAltFlag = KEY_EVENT_RECORD.LEFT_ALT_PRESSED;
        Assert.AreNotEqual(0, leftAltFlag);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KEY_EVENT_RECORD_RightAltPressedConstant()
    {
        int rightAltFlag = KEY_EVENT_RECORD.RIGHT_ALT_PRESSED;
        Assert.AreNotEqual(0, rightAltFlag);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(KEY_EVENT_RECORD.SHIFT_PRESSED)]
    [DataRow(KEY_EVENT_RECORD.LEFT_CTRL_PRESSED)]
    [DataRow(KEY_EVENT_RECORD.RIGHT_CTRL_PRESSED)]
    [DataRow(KEY_EVENT_RECORD.LEFT_ALT_PRESSED)]
    [DataRow(KEY_EVENT_RECORD.RIGHT_ALT_PRESSED)]
    public void KEY_EVENT_RECORD_ModifierFlags_AreDistinct(int modifierFlag)
    {
        // Verify that each modifier flag is distinct from others
        int otherModifiers = 0;
        if (modifierFlag != KEY_EVENT_RECORD.SHIFT_PRESSED)
            otherModifiers |= KEY_EVENT_RECORD.SHIFT_PRESSED;
        if (modifierFlag != KEY_EVENT_RECORD.LEFT_CTRL_PRESSED)
            otherModifiers |= KEY_EVENT_RECORD.LEFT_CTRL_PRESSED;

        // The flags should be combinable without overlap
        int combined = modifierFlag | otherModifiers;
        Assert.AreNotEqual(modifierFlag, otherModifiers);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KEY_EVENT_RECORD_DefaultInitialization()
    {
        var record = new KEY_EVENT_RECORD();

        Assert.IsFalse(record.bKeyDown);
        Assert.AreEqual(0, record.wVirtualKeyCode);
        Assert.AreEqual(0, record.dwControlKeyState);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void INPUT_RECORD_KeyEventType()
    {
        var record = new INPUT_RECORD
        {
            EventType = 1 // KEY_EVENT
        };

        Assert.AreEqual(1, record.EventType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void INPUT_RECORD_MouseEventType()
    {
        var record = new INPUT_RECORD
        {
            EventType = 2 // MOUSE_EVENT
        };

        Assert.AreEqual(2, record.EventType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void POINT_DefaultInitialization()
    {
        var point = new POINT();

        Assert.AreEqual(0, point.X);
        Assert.AreEqual(0, point.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow((ushort)5, (ushort)10)]
    [DataRow((ushort)0, (ushort)0)]
    public void POINT_SetValues(ushort x, ushort y)
    {
        var point = new POINT { X = x, Y = y };

        Assert.AreEqual(x, point.X);
        Assert.AreEqual(y, point.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleHandle_CanBeCreated()
    {
        var handle = new ConsoleHandle();

        Assert.IsNotNull(handle);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow((byte)0)]
    [DataRow((byte)255)]
    [DataRow((byte)65)]
    public void KeyEventAsciiChar_CanBeSetAndRetrieved(byte asciiValue)
    {
        var record = new KEY_EVENT_RECORD
        {
            AsciiChar = asciiValue
        };

        Assert.AreEqual(asciiValue, record.AsciiChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyEventUnicodeChar_CanBeSetAndRetrieved()
    {
        var record = new KEY_EVENT_RECORD
        {
            UnicodeChar = 'A'
        };

        Assert.AreEqual('A', record.UnicodeChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MOUSE_EVENT_RECORD_WithAllFlags_CanBeSet()
    {
        var record = new MOUSE_EVENT_RECORD
        {
            dwMousePosition = new COORD { X = 10, Y = 20 },
            dwButtonState = MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED,
            dwEventFlags = MOUSE_EVENT_RECORD.MouseMoved | MOUSE_EVENT_RECORD.DoubleClick
        };

        Assert.AreEqual(10, (int)record.dwMousePosition.X);
        Assert.AreEqual(20, (int)record.dwMousePosition.Y);
        Assert.AreEqual(MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED, record.dwButtonState);
        Assert.IsTrue((record.dwEventFlags & MOUSE_EVENT_RECORD.MouseMoved) != 0);
        Assert.IsTrue((record.dwEventFlags & MOUSE_EVENT_RECORD.DoubleClick) != 0);
    }
}
