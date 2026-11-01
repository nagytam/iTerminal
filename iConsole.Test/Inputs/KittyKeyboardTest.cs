using System.Text;
using iConsole.Data;
using iConsole.Inputs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Inputs;

[TestClass]
public class KittyKeyboardTest
{
    private static List<KeyEvent> Decode(string input, out TerminalInputUtf8Decoder decoder)
    {
        decoder = new TerminalInputUtf8Decoder();
        List<KeyEvent> keys = [];
        decoder.Append(Encoding.UTF8.GetBytes(input));
        decoder.Drain(keys.Add, _ => { });
        return keys;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sequences_AreTheDocumentedOnes()
    {
        string query = KittyKeyboardProtocol.Query;
        Assert.AreEqual("\x1b[?u", query);
        Assert.AreEqual("\x1b[>1u", KittyKeyboardProtocol.Push);
        string pop = KittyKeyboardProtocol.Pop;
        Assert.AreEqual("\x1b[<u", pop);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Reply_MarksTheTerminalAsSupporting_AndEmitsNoKey()
    {
        List<KeyEvent> keys = Decode("\x1b[?0u", out TerminalInputUtf8Decoder decoder);

        Assert.IsTrue(decoder.KittyKeyboardSupported);
        Assert.AreEqual(0, keys.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoReply_LeavesSupportOff()
    {
        Decode("a", out TerminalInputUtf8Decoder decoder);

        Assert.IsFalse(decoder.KittyKeyboardSupported);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Escape_IsEscape()
    {
        KeyEvent key = Decode("\x1b[27u", out _).Single();

        Assert.AreEqual(ConsoleKey.Escape, key.ConsoleKey);
        Assert.IsFalse(key.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CtrlI_IsNotTab()
    {
        KeyEvent key = Decode("\x1b[105;5u", out _).Single();

        Assert.AreEqual(ConsoleKey.I, key.ConsoleKey);
        Assert.IsTrue(key.Control);
        Assert.IsFalse(key.Shift);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CtrlM_IsNotEnter()
    {
        KeyEvent key = Decode("\x1b[109;5u", out _).Single();

        Assert.AreEqual(ConsoleKey.M, key.ConsoleKey);
        Assert.IsTrue(key.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CtrlShiftLetter_KeepsBothModifiers()
    {
        KeyEvent key = Decode("\x1b[112;6u", out _).Single();

        Assert.AreEqual(ConsoleKey.P, key.ConsoleKey);
        Assert.IsTrue(key.Control);
        Assert.IsTrue(key.Shift);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CtrlLetter_CarriesTheControlCharacterLikeTheLegacyDecoder()
    {
        KeyEvent kitty = Decode("\x1b[97;5u", out _).Single();
        KeyEvent legacy = Decode("\x01", out _).Single();

        Assert.AreEqual(legacy.KeyChar, kitty.KeyChar);
        Assert.AreEqual(legacy.ConsoleKey, kitty.ConsoleKey);
        Assert.AreEqual(legacy.Control, kitty.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CtrlEnter_AndShiftEnter_AreTold_Apart()
    {
        KeyEvent ctrl = Decode("\x1b[13;5u", out _).Single();
        KeyEvent shift = Decode("\x1b[13;2u", out _).Single();

        Assert.AreEqual(ConsoleKey.Enter, ctrl.ConsoleKey);
        Assert.IsTrue(ctrl.Control);
        Assert.IsFalse(ctrl.Shift);
        Assert.AreEqual(ConsoleKey.Enter, shift.ConsoleKey);
        Assert.IsTrue(shift.Shift);
        Assert.IsFalse(shift.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AltLetter_IsAlt()
    {
        KeyEvent key = Decode("\x1b[120;3u", out _).Single();

        Assert.AreEqual(ConsoleKey.X, key.ConsoleKey);
        Assert.IsTrue(key.Alt);
        Assert.AreEqual('x', key.KeyChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ShiftLetter_WithShiftedCode_UsesTheShiftedCharacter()
    {
        KeyEvent key = Decode("\x1b[97:65;2u", out _).Single();

        Assert.AreEqual(ConsoleKey.A, key.ConsoleKey);
        Assert.IsTrue(key.Shift);
        Assert.AreEqual('A', key.KeyChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Digit_MapsToDigitKey()
    {
        KeyEvent key = Decode("\x1b[49;5u", out _).Single();

        Assert.AreEqual(ConsoleKey.D1, key.ConsoleKey);
        Assert.IsTrue(key.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CapsLockAndNumLockBits_AreIgnored()
    {
        // 1 + ctrl(4) + caps(64) = 69
        KeyEvent key = Decode("\x1b[97;69u", out _).Single();

        Assert.IsTrue(key.Control);
        Assert.IsFalse(key.Shift);
        Assert.IsFalse(key.Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyRelease_IsSwallowed()
    {
        List<KeyEvent> keys = Decode("\x1b[97;5:3u", out _);

        Assert.AreEqual(0, keys.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyRepeat_IsAPress()
    {
        KeyEvent key = Decode("\x1b[97;5:2u", out _).Single();

        Assert.AreEqual(ConsoleKey.A, key.ConsoleKey);
        Assert.AreEqual(KeyPressType.Down, key.KeyPressType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PrivateUseCode_IsSwallowedAndNextKeyStillRead()
    {
        List<KeyEvent> keys = Decode("\x1b[57399;1ux", out _);

        Assert.AreEqual(1, keys.Count);
        Assert.AreEqual('x', keys[0].KeyChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SequenceSplitAcrossReads_WaitsForTheRest()
    {
        TerminalInputUtf8Decoder decoder = new();
        List<KeyEvent> keys = [];
        decoder.Append("\x1b[105;"u8);
        decoder.Drain(keys.Add, _ => { });
        Assert.AreEqual(0, keys.Count);

        decoder.Append("5u"u8);
        decoder.Drain(keys.Add, _ => { });

        Assert.AreEqual(1, keys.Count);
        Assert.AreEqual(ConsoleKey.I, keys[0].ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LegacyKeys_StillDecode()
    {
        List<KeyEvent> keys = Decode("\x1b[A\t", out _);

        Assert.AreEqual(ConsoleKey.UpArrow, keys[0].ConsoleKey);
        Assert.AreEqual(ConsoleKey.Tab, keys[1].ConsoleKey);
    }
}
