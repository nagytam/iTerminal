using iConsole.Data;
using iConsole.Inputs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Inputs;

[TestClass]
public class TerminalInputUtf8DecoderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Drain_ParsesSgrLeftPress()
    {
        var d = new TerminalInputUtf8Decoder();
        MouseEvent? m = null;
        d.Append("\x1b[<0;5;10M"u8);
        d.Drain(_ => { }, x => m = x);
        Assert.IsNotNull(m);
        Assert.AreEqual(4, m!.Position.X);
        Assert.AreEqual(9, m.Position.Y);
        Assert.IsTrue(m.LeftClicked);
        Assert.IsFalse(m.RightClicked);
        Assert.IsFalse(m.IsRelease);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Drain_ParsesSgrRelease()
    {
        var d = new TerminalInputUtf8Decoder();
        MouseEvent? m = null;
        d.Append("\x1b[<0;5;10m"u8);
        d.Drain(_ => { }, x => m = x);
        Assert.IsNotNull(m);
        Assert.IsTrue(m!.IsRelease);
        Assert.IsFalse(m.LeftClicked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Drain_ParsesArrowUp()
    {
        var d = new TerminalInputUtf8Decoder();
        KeyEvent? k = null;
        d.Append("\x1b[A"u8);
        d.Drain(x => k = x, _ => { });
        Assert.IsNotNull(k);
        Assert.AreEqual(ConsoleKey.UpArrow, k!.ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Drain_ParsesWheelUp()
    {
        var d = new TerminalInputUtf8Decoder();
        MouseEvent? m = null;
        d.Append("\x1b[<64;1;1M"u8);
        d.Drain(_ => { }, x => m = x);
        Assert.IsNotNull(m);
        Assert.AreEqual(1, m!.WheelDelta);
        Assert.IsFalse(m.LeftClicked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Drain_MotionWithLeftHeld_DoesNotSetLeftClicked()
    {
        var d = new TerminalInputUtf8Decoder();
        MouseEvent? m = null;
        d.Append("\x1b[<32;5;10M"u8);
        d.Drain(_ => { }, x => m = x);
        Assert.IsNotNull(m);
        Assert.IsFalse(m!.LeftClicked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Drain_ParsesCsiPageUp()
    {
        var d = new TerminalInputUtf8Decoder();
        KeyEvent? k = null;
        d.Append("\x1b[5~"u8);
        d.Drain(x => k = x, _ => { });
        Assert.IsNotNull(k);
        Assert.AreEqual(ConsoleKey.PageUp, k!.ConsoleKey);
        Assert.IsFalse(k.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Drain_ParsesCsiCtrlPageUp_xtermStyleModifier()
    {
        var d = new TerminalInputUtf8Decoder();
        KeyEvent? k = null;
        d.Append("\x1b[5;5~"u8);
        d.Drain(x => k = x, _ => { });
        Assert.IsNotNull(k);
        Assert.AreEqual(ConsoleKey.PageUp, k!.ConsoleKey);
        Assert.IsTrue(k.Control, "CSI 5;5~ should set Control (xterm modifier 5).");
        Assert.IsFalse(k.Shift);
        Assert.IsFalse(k.Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("\x1b[11~", ConsoleKey.F1)]
    [DataRow("\x1b[12~", ConsoleKey.F2)]
    [DataRow("\x1b[13~", ConsoleKey.F3)]
    [DataRow("\x1b[14~", ConsoleKey.F4)]
    [DataRow("\x1b[15~", ConsoleKey.F5)]
    [DataRow("\x1b[17~", ConsoleKey.F6)]
    [DataRow("\x1b[18~", ConsoleKey.F7)]
    [DataRow("\x1b[19~", ConsoleKey.F8)]
    [DataRow("\x1b[20~", ConsoleKey.F9)]
    [DataRow("\x1b[21~", ConsoleKey.F10)]
    [DataRow("\x1b[23~", ConsoleKey.F11)]
    [DataRow("\x1b[24~", ConsoleKey.F12)]
    public void Drain_ParsesCsiFunctionKey(string sequence, ConsoleKey expected)
    {
        var d = new TerminalInputUtf8Decoder();
        KeyEvent? k = null;
        d.Append(System.Text.Encoding.UTF8.GetBytes(sequence));
        d.Drain(x => k = x, _ => { });
        Assert.IsNotNull(k, $"Expected {expected} from {sequence}");
        Assert.AreEqual(expected, k!.ConsoleKey);
        Assert.IsFalse(k.Control);
        Assert.IsFalse(k.Shift);
        Assert.IsFalse(k.Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow((byte)'P', ConsoleKey.F1)]
    [DataRow((byte)'Q', ConsoleKey.F2)]
    [DataRow((byte)'R', ConsoleKey.F3)]
    [DataRow((byte)'S', ConsoleKey.F4)]
    public void Drain_ParsesSs3FunctionKey(byte finalByte, ConsoleKey expected)
    {
        var d = new TerminalInputUtf8Decoder();
        KeyEvent? k = null;
        d.Append(new byte[] { 0x1b, (byte)'O', finalByte });
        d.Drain(x => k = x, _ => { });
        Assert.IsNotNull(k, $"Expected {expected} from ESC O {(char)finalByte}");
        Assert.AreEqual(expected, k!.ConsoleKey);
    }

    // xterm sends the modified counterpart of an unmodified single-letter sequence as
    // "CSI 1 ; Pm <letter>" rather than a tilde-terminated one — e.g. Ctrl+Right is
    // "\x1b[1;5C", Ctrl+F1 is "\x1b[1;5P". Before this coverage existed, TryParseCsiKey had no
    // branch for this shape at all, so every Ctrl/Shift/Alt+arrow and Ctrl/Shift/Alt+F1-F4 chord
    // was silently discarded on Linux.
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("\x1b[1;5C", ConsoleKey.RightArrow)]
    [DataRow("\x1b[1;5D", ConsoleKey.LeftArrow)]
    [DataRow("\x1b[1;5A", ConsoleKey.UpArrow)]
    [DataRow("\x1b[1;5B", ConsoleKey.DownArrow)]
    [DataRow("\x1b[1;5H", ConsoleKey.Home)]
    [DataRow("\x1b[1;5F", ConsoleKey.End)]
    [DataRow("\x1b[1;5P", ConsoleKey.F1)]
    [DataRow("\x1b[1;5Q", ConsoleKey.F2)]
    [DataRow("\x1b[1;5R", ConsoleKey.F3)]
    [DataRow("\x1b[1;5S", ConsoleKey.F4)]
    public void Drain_ParsesCtrlModifiedCsiLetterKey(string sequence, ConsoleKey expected)
    {
        var d = new TerminalInputUtf8Decoder();
        KeyEvent? k = null;
        d.Append(System.Text.Encoding.UTF8.GetBytes(sequence));
        d.Drain(x => k = x, _ => { });
        Assert.IsNotNull(k, $"Expected {expected} from {sequence}");
        Assert.AreEqual(expected, k!.ConsoleKey);
        Assert.IsTrue(k.Control, $"{sequence} should set Control (xterm modifier 5).");
        Assert.IsFalse(k.Shift);
        Assert.IsFalse(k.Alt);
    }

    // Terminals that do not send "CSI 1 ; 5 <letter>" for Ctrl+arrow: rxvt sends the lower-case
    // letters in SS3 form, and some send the modifier as a digit inside it ("ESC O 5 A").
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("\x1bOa", ConsoleKey.UpArrow)]
    [DataRow("\x1bOb", ConsoleKey.DownArrow)]
    [DataRow("\x1bOc", ConsoleKey.RightArrow)]
    [DataRow("\x1bOd", ConsoleKey.LeftArrow)]
    [DataRow("\x1bO5A", ConsoleKey.UpArrow)]
    [DataRow("\x1bO5B", ConsoleKey.DownArrow)]
    [DataRow("\x1bO5C", ConsoleKey.RightArrow)]
    [DataRow("\x1bO5D", ConsoleKey.LeftArrow)]
    public void Drain_ParsesCtrlArrowInTheSs3Forms(string sequence, ConsoleKey expected)
    {
        var d = new TerminalInputUtf8Decoder();
        var keys = new List<KeyEvent>();
        d.Append(System.Text.Encoding.UTF8.GetBytes(sequence));
        d.Drain(keys.Add, _ => { });
        Assert.AreEqual(1, keys.Count, $"{sequence} should be one key, not Alt+Shift+O and the letters after it.");
        Assert.AreEqual(expected, keys[0].ConsoleKey);
        Assert.IsTrue(keys[0].Control, $"{sequence} should set Control.");
        Assert.IsFalse(keys[0].Shift);
        Assert.IsFalse(keys[0].Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Drain_WaitsForTheLetterOfAnSs3ModifierDigit()
    {
        var d = new TerminalInputUtf8Decoder();
        var keys = new List<KeyEvent>();
        d.Append(System.Text.Encoding.UTF8.GetBytes("\x1bO5"));
        d.Drain(keys.Add, _ => { });
        Assert.AreEqual(0, keys.Count, "ESC O 5 is the start of an arrow; nothing is decided until the letter arrives.");

        d.Append(System.Text.Encoding.UTF8.GetBytes("A"));
        d.Drain(keys.Add, _ => { });
        Assert.AreEqual(1, keys.Count);
        Assert.AreEqual(ConsoleKey.UpArrow, keys[0].ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Drain_ParsesShiftAltModifiedCsiLetterKey()
    {
        var d = new TerminalInputUtf8Decoder();
        KeyEvent? k = null;
        d.Append("\x1b[1;4C"u8); // xterm modifier 4 = Shift+Alt
        d.Drain(x => k = x, _ => { });
        Assert.IsNotNull(k);
        Assert.AreEqual(ConsoleKey.RightArrow, k!.ConsoleKey);
        Assert.IsTrue(k.Shift);
        Assert.IsTrue(k.Alt);
        Assert.IsFalse(k.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DrainStandaloneEscape_EmitsEscapeWhenOnlyByteIs0x1b()
    {
        var d = new TerminalInputUtf8Decoder();
        d.Append([0x1b]);
        d.Drain(_ => Assert.Fail("Drain should not emit without a second byte"), _ => { });

        KeyEvent? k = null;
        d.DrainStandaloneEscape(x => k = x);

        Assert.IsNotNull(k);
        Assert.AreEqual(ConsoleKey.Escape, k!.ConsoleKey);
        Assert.IsFalse(k.Control);
        Assert.IsFalse(k.Shift);
        Assert.IsFalse(k.Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DrainStandaloneEscape_DoesNotEmitWhenBufferIsEmpty()
    {
        var d = new TerminalInputUtf8Decoder();
        d.DrainStandaloneEscape(_ => Assert.Fail("Should not emit when buffer is empty"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DrainStandaloneEscape_DoesNotEmitWhenMoreBytesFollow()
    {
        var d = new TerminalInputUtf8Decoder();
        d.Append("\x1b[A"u8); // up-arrow — should be consumed by Drain, not DrainStandaloneEscape
        d.Drain(_ => { }, _ => { });
        d.DrainStandaloneEscape(_ => Assert.Fail("Should not emit after a full sequence was consumed"));
    }
}
