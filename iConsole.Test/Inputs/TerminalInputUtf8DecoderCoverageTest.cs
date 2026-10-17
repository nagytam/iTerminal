using System.Text;
using iConsole.Data;
using iConsole.Inputs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Inputs;

[TestClass]
public class TerminalInputUtf8DecoderCoverageTest
{
    private static (List<KeyEvent> Keys, List<MouseEvent> Mice) Decode(string input, bool standaloneEscape = false)
    {
        var d = new TerminalInputUtf8Decoder();
        var keys = new List<KeyEvent>();
        var mice = new List<MouseEvent>();
        d.Append(Encoding.UTF8.GetBytes(input));
        d.Drain(keys.Add, mice.Add);
        if (standaloneEscape)
        {
            d.DrainStandaloneEscape(keys.Add);
        }

        return (keys, mice);
    }

    [TestMethod]
    [DataRow("\x1b[A", ConsoleKey.UpArrow)]
    [DataRow("\x1b[B", ConsoleKey.DownArrow)]
    [DataRow("\x1b[C", ConsoleKey.RightArrow)]
    [DataRow("\x1b[D", ConsoleKey.LeftArrow)]
    [DataRow("\x1b[H", ConsoleKey.Home)]
    [DataRow("\x1b[F", ConsoleKey.End)]
    [DataRow("\x1bOA", ConsoleKey.UpArrow)]
    [DataRow("\x1bOB", ConsoleKey.DownArrow)]
    [DataRow("\x1bOC", ConsoleKey.RightArrow)]
    [DataRow("\x1bOD", ConsoleKey.LeftArrow)]
    [DataRow("\x1bOH", ConsoleKey.Home)]
    [DataRow("\x1bOF", ConsoleKey.End)]
    [DataRow("\x1bOP", ConsoleKey.F1)]
    [DataRow("\x1bOQ", ConsoleKey.F2)]
    [DataRow("\x1bOR", ConsoleKey.F3)]
    [DataRow("\x1bOS", ConsoleKey.F4)]
    [DataRow("\x1b[1~", ConsoleKey.Home)]
    [DataRow("\x1b[2~", ConsoleKey.Insert)]
    [DataRow("\x1b[3~", ConsoleKey.Delete)]
    [DataRow("\x1b[4~", ConsoleKey.End)]
    [DataRow("\x1b[6~", ConsoleKey.PageDown)]
    public void Drain_PlainSequence_EmitsUnmodifiedKey(string sequence, ConsoleKey expected)
    {
        (List<KeyEvent> keys, _) = Decode(sequence);
        Assert.HasCount(1, keys);
        Assert.AreEqual(expected, keys[0].ConsoleKey);
        Assert.IsFalse(keys[0].Shift || keys[0].Alt || keys[0].Control);
    }

    [TestMethod]
    [DataRow("\x1b[1;2A", ConsoleKey.UpArrow, true, false, false)]
    [DataRow("\x1b[1;3B", ConsoleKey.DownArrow, false, true, false)]
    [DataRow("\x1b[1;4C", ConsoleKey.RightArrow, true, true, false)]
    [DataRow("\x1b[1;5D", ConsoleKey.LeftArrow, false, false, true)]
    [DataRow("\x1b[1;6H", ConsoleKey.Home, true, false, true)]
    [DataRow("\x1b[1;7F", ConsoleKey.End, false, true, true)]
    [DataRow("\x1b[1;8P", ConsoleKey.F1, true, true, true)]
    [DataRow("\x1b[1;5Q", ConsoleKey.F2, false, false, true)]
    [DataRow("\x1b[1;5R", ConsoleKey.F3, false, false, true)]
    [DataRow("\x1b[1;5S", ConsoleKey.F4, false, false, true)]
    [DataRow("\x1b[1;1A", ConsoleKey.UpArrow, false, false, false)]
    [DataRow("\x1b[1;9A", ConsoleKey.UpArrow, false, false, false)]
    [DataRow("\x1b[5A", ConsoleKey.UpArrow, false, false, true)]
    [DataRow("\x1b[3;2~", ConsoleKey.Delete, true, false, false)]
    public void Drain_ModifiedSequence_AppliesXtermModifier(string sequence, ConsoleKey key, bool shift, bool alt, bool control)
    {
        (List<KeyEvent> keys, _) = Decode(sequence);
        Assert.HasCount(1, keys);
        Assert.AreEqual(key, keys[0].ConsoleKey);
        Assert.AreEqual(shift, keys[0].Shift);
        Assert.AreEqual(alt, keys[0].Alt);
        Assert.AreEqual(control, keys[0].Control);
    }

    [TestMethod]
    [DataRow("\x1b[1;;5A")]
    [DataRow("\x1b[7;5~")]
    [DataRow("\x1b[1;;5~")]
    [DataRow("\x1b[99~")]
    [DataRow("\x1b[X")]
    [DataRow("\x1b[1;5X")]
    [DataRow("\x1b[200Z")]
    [DataRow("\x1b[<0;5M")]
    [DataRow("\x1b[<1;5;;M")]
    [DataRow("\x1b[<0;5;10x")]
    public void Drain_UnknownSequence_IsSwallowed(string sequence)
    {
        (List<KeyEvent> keys, List<MouseEvent> mice) = Decode(sequence);
        Assert.IsEmpty(keys);
        Assert.IsEmpty(mice);
    }

    [TestMethod]
    public void Drain_CsiOneZ_IsShiftTab()
    {
        (List<KeyEvent> keys, _) = Decode("[1Z");
        Assert.HasCount(1, keys);
        Assert.AreEqual(ConsoleKey.Tab, keys[0].ConsoleKey);
        Assert.IsTrue(keys[0].Shift);
    }

    [TestMethod]
    [DataRow("\x1bOa", ConsoleKey.UpArrow)]
    [DataRow("\x1bOb", ConsoleKey.DownArrow)]
    [DataRow("\x1bOc", ConsoleKey.RightArrow)]
    [DataRow("\x1bOd", ConsoleKey.LeftArrow)]
    public void Drain_RxvtCtrlArrow_SetsControl(string sequence, ConsoleKey expected)
    {
        (List<KeyEvent> keys, _) = Decode(sequence);
        Assert.HasCount(1, keys);
        Assert.AreEqual(expected, keys[0].ConsoleKey);
        Assert.IsTrue(keys[0].Control);
    }

    [TestMethod]
    public void Drain_Ss3ModifiedArrow_AppliesModifier()
    {
        (List<KeyEvent> keys, _) = Decode("\x1bO5A");
        Assert.HasCount(1, keys);
        Assert.AreEqual(ConsoleKey.UpArrow, keys[0].ConsoleKey);
        Assert.IsTrue(keys[0].Control);
    }

    [TestMethod]
    public void Drain_Ss3ModifierDigitWithoutLetter_WaitsForMoreInput()
    {
        var d = new TerminalInputUtf8Decoder();
        var keys = new List<KeyEvent>();
        d.Append("\x1bO5"u8);
        d.Drain(keys.Add, _ => { });
        Assert.IsEmpty(keys);

        d.Append("B"u8);
        d.Drain(keys.Add, _ => { });
        Assert.HasCount(1, keys);
        Assert.AreEqual(ConsoleKey.DownArrow, keys[0].ConsoleKey);
    }

    [TestMethod]
    public void Drain_Ss3ModifierDigitFollowedByNonArrow_FallsBackToAltO()
    {
        (List<KeyEvent> keys, _) = Decode("\x1bO5Z");
        Assert.IsNotEmpty(keys);
        Assert.IsTrue(keys[0].Alt);
        Assert.AreEqual('O', keys[0].KeyChar);
    }

    [TestMethod]
    public void Drain_AltLetter_SetsAlt()
    {
        (List<KeyEvent> keys, _) = Decode("\x1bx");
        Assert.HasCount(1, keys);
        Assert.AreEqual(ConsoleKey.X, keys[0].ConsoleKey);
        Assert.IsTrue(keys[0].Alt);
        Assert.IsFalse(keys[0].Shift);
    }

    [TestMethod]
    public void Drain_AltUpperCaseLetterAndDigit_KeepShiftAndDigitKeys()
    {
        (List<KeyEvent> keys, _) = Decode("\x1bX\u001b7\x1b%");
        Assert.HasCount(3, keys);
        Assert.AreEqual(ConsoleKey.X, keys[0].ConsoleKey);
        Assert.IsTrue(keys[0].Shift);
        Assert.IsTrue(keys[0].Alt);
        Assert.AreEqual(ConsoleKey.D7, keys[1].ConsoleKey);
        Assert.AreEqual(ConsoleKey.None, keys[2].ConsoleKey);
        Assert.IsTrue(keys[2].Alt);
    }

    [TestMethod]
    public void Drain_DoubleEscapeThenChar_IsAltChar()
    {
        (List<KeyEvent> keys, _) = Decode("\x1b\x1bq");
        Assert.HasCount(1, keys);
        Assert.AreEqual(ConsoleKey.Q, keys[0].ConsoleKey);
        Assert.IsTrue(keys[0].Alt);
    }

    [TestMethod]
    public void Drain_DoubleEscapeThenControlByte_KeepsTheControlKey()
    {
        (List<KeyEvent> keys, _) = Decode("\x1b\x1b\x01");
        Assert.HasCount(1, keys);
        Assert.IsTrue(keys[0].Control);
    }

    [TestMethod]
    public void Drain_DoubleEscapeAlone_WaitsForMoreInput()
    {
        (List<KeyEvent> keys, _) = Decode("\x1b\x1b");
        Assert.IsEmpty(keys);
    }

    [TestMethod]
    public void Drain_EscapeThenControlByte_DropsTheEscape()
    {
        (List<KeyEvent> keys, _) = Decode("\x1b\x01");
        Assert.HasCount(1, keys);
        Assert.IsTrue(keys[0].Control);
    }

    [TestMethod]
    public void Drain_LoneEscape_WaitsUntilStandaloneEscapeIsDrained()
    {
        (List<KeyEvent> keys, _) = Decode("\x1b");
        Assert.IsEmpty(keys);

        (keys, _) = Decode("\x1b", standaloneEscape: true);
        Assert.HasCount(1, keys);
        Assert.AreEqual(ConsoleKey.Escape, keys[0].ConsoleKey);
    }

    [TestMethod]
    public void DrainStandaloneEscape_IgnoresLongerBuffers()
    {
        (List<KeyEvent> keys, _) = Decode("\x1b[", standaloneEscape: true);
        Assert.IsEmpty(keys);
    }

    [TestMethod]
    public void Drain_IncompleteCsi_WaitsForTerminator()
    {
        var d = new TerminalInputUtf8Decoder();
        var keys = new List<KeyEvent>();
        d.Append("\x1b[1;"u8);
        d.Drain(keys.Add, _ => { });
        Assert.IsEmpty(keys);

        d.Append("5C"u8);
        d.Drain(keys.Add, _ => { });
        Assert.HasCount(1, keys);
        Assert.AreEqual(ConsoleKey.RightArrow, keys[0].ConsoleKey);
        Assert.IsTrue(keys[0].Control);
    }

    [TestMethod]
    public void Drain_PlainControlCharacters_MapToEnterTabBackspaceAndCtrlLetters()
    {
        (List<KeyEvent> keys, _) = Decode("\r\n\t\x7f\x01\x1a");
        Assert.HasCount(6, keys);
        Assert.AreEqual(ConsoleKey.Enter, keys[0].ConsoleKey);
        Assert.AreEqual(ConsoleKey.Enter, keys[1].ConsoleKey);
        Assert.AreEqual(ConsoleKey.Tab, keys[2].ConsoleKey);
        Assert.AreEqual(ConsoleKey.Backspace, keys[3].ConsoleKey);
        Assert.AreEqual(ConsoleKey.A, keys[4].ConsoleKey);
        Assert.IsTrue(keys[4].Control);
        Assert.AreEqual(ConsoleKey.Z, keys[5].ConsoleKey);
        Assert.IsTrue(keys[5].Control);
    }

    [TestMethod]
    public void Drain_ControlByteWithoutLetter_HasNoConsoleKey()
    {
        (List<KeyEvent> keys, _) = Decode("\x1c");
        Assert.HasCount(1, keys);
        Assert.AreEqual(ConsoleKey.None, keys[0].ConsoleKey);
        Assert.IsTrue(keys[0].Control);
    }

    [TestMethod]
    public void Drain_PrintableAscii_MapsLettersDigitsAndSymbols()
    {
        (List<KeyEvent> keys, _) = Decode("aZ5!");
        Assert.HasCount(4, keys);
        Assert.AreEqual(ConsoleKey.A, keys[0].ConsoleKey);
        Assert.IsFalse(keys[0].Shift);
        Assert.AreEqual(ConsoleKey.Z, keys[1].ConsoleKey);
        Assert.IsTrue(keys[1].Shift);
        Assert.AreEqual(ConsoleKey.D5, keys[2].ConsoleKey);
        Assert.AreEqual(ConsoleKey.None, keys[3].ConsoleKey);
        Assert.AreEqual('!', keys[3].KeyChar);
    }

    [TestMethod]
    public void Drain_MultiByteUtf8_EmitsTheCharacter()
    {
        (List<KeyEvent> keys, _) = Decode("é€");
        Assert.HasCount(2, keys);
        Assert.AreEqual('é', keys[0].KeyChar);
        Assert.AreEqual('€', keys[1].KeyChar);
    }

    [TestMethod]
    public void Drain_SurrogatePairRune_EmitsFirstUtf16Unit()
    {
        (List<KeyEvent> keys, _) = Decode("😀");
        Assert.HasCount(1, keys);
        Assert.AreEqual("😀"[0], keys[0].KeyChar);
    }

    [TestMethod]
    public void Drain_SplitMultiByteCharacter_WaitsForRemainingBytes()
    {
        var d = new TerminalInputUtf8Decoder();
        var keys = new List<KeyEvent>();
        d.Append([0xE2, 0x82]);
        d.Drain(keys.Add, _ => { });
        Assert.IsEmpty(keys);

        d.Append([0xAC]);
        d.Drain(keys.Add, _ => { });
        Assert.HasCount(1, keys);
        Assert.AreEqual('€', keys[0].KeyChar);
    }

    [TestMethod]
    public void Drain_InvalidUtf8_StaysBufferedWithoutEmitting()
    {
        var d = new TerminalInputUtf8Decoder();
        var keys = new List<KeyEvent>();
        d.Append([0x80]);
        d.Drain(keys.Add, _ => { });
        Assert.IsEmpty(keys);

        d.Append([0xE2, 0x28, 0xA1]);
        d.Drain(keys.Add, _ => { });
        Assert.IsEmpty(keys);
    }

    [TestMethod]
    public void Drain_SgrRightPressAndWheelDown_SetFlags()
    {
        (_, List<MouseEvent> mice) = Decode("\x1b[<2;3;4M\x1b[<65;1;1M\x1b[<64;1;1m");
        Assert.HasCount(3, mice);
        Assert.IsTrue(mice[0].RightClicked);
        Assert.IsFalse(mice[0].LeftClicked);
        Assert.AreEqual(-1, mice[1].WheelDelta);
        Assert.AreEqual(0, mice[2].WheelDelta, "A release never scrolls.");
        Assert.IsTrue(mice[2].IsRelease);
    }
}
