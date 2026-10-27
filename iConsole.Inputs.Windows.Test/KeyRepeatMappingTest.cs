using iConsole.Data;
using iConsole.Inputs;
using iConsole.Inputs.Windows.Data;
using iConsole.Inputs.Windows.Jobs;

namespace iConsole.Inputs.Windows.Test;

/// <summary>A held key is a repeat, a record with a repeat count is that many presses, and a first press is neither.</summary>
[TestClass]
public class KeyRepeatMappingTest
{
    private const ushort VkA = 0x41;
    private const ushort VkLeft = 0x25;

    private static KEY_EVENT_RECORD Letter(bool down, ushort repeatCount = 1, char c = 'a') => new()
    {
        bKeyDown = down,
        wRepeatCount = repeatCount,
        wVirtualKeyCode = VkA,
        UnicodeChar = c,
    };

    private static KEY_EVENT_RECORD Arrow(bool down, ushort repeatCount = 1) => new()
    {
        bKeyDown = down,
        wRepeatCount = repeatCount,
        wVirtualKeyCode = VkLeft,
        UnicodeChar = '\0',
    };

    private static List<KeyEvent> Map(KeyRepeatTracker tracker, KEY_EVENT_RECORD record) =>
        [.. MouseEventPublisherJob.MapKeyRecord(record, tracker)];

    [TestMethod]
    public void AFirstPress_IsNotARepeat()
    {
        List<KeyEvent> events = Map(new KeyRepeatTracker(), Letter(down: true));

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual('a', events[0].KeyChar);
        Assert.IsFalse(events[0].IsRepeat);
    }

    [TestMethod]
    public void TheSecondAndLaterKeyDownsOfAHeldKey_AreRepeats()
    {
        var tracker = new KeyRepeatTracker();

        Assert.IsFalse(Map(tracker, Letter(down: true))[0].IsRepeat);
        Assert.IsTrue(Map(tracker, Letter(down: true))[0].IsRepeat);
        Assert.IsTrue(Map(tracker, Letter(down: true))[0].IsRepeat);
    }

    [TestMethod]
    public void LettingGo_EndsTheRepeat_SoTheNextPressIsFresh()
    {
        var tracker = new KeyRepeatTracker();
        Map(tracker, Letter(down: true));
        Map(tracker, Letter(down: true));

        List<KeyEvent> release = Map(tracker, Letter(down: false));
        List<KeyEvent> again = Map(tracker, Letter(down: true));

        Assert.AreEqual(0, release.Count, "a key with a character has no key-up event, as before");
        Assert.IsFalse(again[0].IsRepeat);
    }

    [TestMethod]
    public void ARecordWithARepeatCount_StandsForThatManyPresses_AllButTheFirstRepeats()
    {
        List<KeyEvent> events = Map(new KeyRepeatTracker(), Letter(down: true, repeatCount: 4));

        Assert.AreEqual(4, events.Count);
        Assert.IsFalse(events[0].IsRepeat);
        Assert.IsTrue(events.Skip(1).All(e => e.IsRepeat));
        Assert.IsTrue(events.All(e => e.KeyChar == 'a'));
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 1)]
    [DataRow(7, 7)]
    [DataRow(1000, KeyRepeatTracker.MaxRepeatCount)]
    public void TheRepeatCount_IsHeldBetweenOneAndTheLimit(int count, int expected)
    {
        List<KeyEvent> events = Map(new KeyRepeatTracker(), Letter(down: true, repeatCount: (ushort)count));

        Assert.AreEqual(expected, events.Count);
    }

    [TestMethod]
    public void KeysWithoutACharacter_KeepTheirDownAndUpEvents_AndRepeatToo()
    {
        var tracker = new KeyRepeatTracker();

        List<KeyEvent> first = Map(tracker, Arrow(down: true));
        List<KeyEvent> held = Map(tracker, Arrow(down: true, repeatCount: 3));
        List<KeyEvent> up = Map(tracker, Arrow(down: false));

        Assert.AreEqual(ConsoleKey.LeftArrow, first[0].ConsoleKey);
        Assert.AreEqual(KeyPressType.Down, first[0].KeyPressType);
        Assert.IsFalse(first[0].IsRepeat);
        Assert.AreEqual(3, held.Count);
        Assert.IsTrue(held.All(e => e.IsRepeat && e.KeyPressType == KeyPressType.Down));
        Assert.AreEqual(1, up.Count);
        Assert.AreEqual(KeyPressType.Up, up[0].KeyPressType);
        Assert.IsFalse(up[0].IsRepeat);
    }

    [TestMethod]
    public void TwoKeysHeldAtOnce_AreTrackedApart()
    {
        var tracker = new KeyRepeatTracker();
        Map(tracker, Letter(down: true));

        List<KeyEvent> other = Map(tracker, new KEY_EVENT_RECORD { bKeyDown = true, wRepeatCount = 1, wVirtualKeyCode = 0x42, UnicodeChar = 'b' });
        List<KeyEvent> sameAgain = Map(tracker, Letter(down: true));

        Assert.IsFalse(other[0].IsRepeat, "b was not down before");
        Assert.IsTrue(sameAgain[0].IsRepeat);
    }

    [TestMethod]
    public void ResetForgetsTheKeysThatWereDown()
    {
        var tracker = new KeyRepeatTracker();
        Map(tracker, Letter(down: true));

        tracker.Reset();

        Assert.IsFalse(Map(tracker, Letter(down: true))[0].IsRepeat);
    }

    [TestMethod]
    public void Modifiers_AreCarriedOnEveryEvent()
    {
        KEY_EVENT_RECORD record = Letter(down: true, repeatCount: 2);
        record.dwControlKeyState = KEY_EVENT_RECORD.LEFT_CTRL_PRESSED | KEY_EVENT_RECORD.SHIFT_PRESSED;

        List<KeyEvent> events = Map(new KeyRepeatTracker(), record);

        Assert.IsTrue(events.All(e => e.Control && e.Shift && !e.Alt));
    }
}
