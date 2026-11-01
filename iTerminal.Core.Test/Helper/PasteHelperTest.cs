using iConsole.Services;
using iTerminal.Core.Helper;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Helper;

/// <summary>Paste for the dialogs that edit their own lines: which keys are a paste, how text is cut down to one line, and a slow clipboard.</summary>
// Some tests put text on the ambient clipboard and paste it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class PasteHelperTest
{
    private sealed class SlowClipboard(TaskCompletionSource<string?> gate) : ITextClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => gate.Task;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CtrlVAndShiftInsertArePasteKeys_NothingElseIs()
    {
        Assert.IsTrue(PasteHelper.IsPasteKey(Key(ConsoleKey.V, 'v', control: true)));
        Assert.IsTrue(PasteHelper.IsPasteKey(Key(ConsoleKey.Insert, shift: true)));
        Assert.IsFalse(PasteHelper.IsPasteKey(Key(ConsoleKey.V, 'v')));
        Assert.IsFalse(PasteHelper.IsPasteKey(Key(ConsoleKey.Insert)));
        Assert.IsFalse(PasteHelper.IsPasteKey(Key(ConsoleKey.V, 'v', control: true, alt: true)));
        Assert.IsFalse(PasteHelper.IsPasteKey(Key(ConsoleKey.V, 'V', shift: true, control: true)));
        Assert.IsFalse(PasteHelper.IsPasteKey(Key(ConsoleKey.C, 'c', control: true)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SingleLine_JoinsLinesAndTabsWithSpaces_AndTrims()
    {
        Assert.AreEqual("a b c", PasteHelper.SingleLine("a" + (char)13 + (char)10 + "b" + (char)9 + "c"));
        Assert.AreEqual("a b", PasteHelper.SingleLine("  a" + (char)10 + (char)10 + "b  "));
        Assert.AreEqual(string.Empty, PasteHelper.SingleLine(string.Empty));
        Assert.AreEqual(string.Empty, PasteHelper.SingleLine((char)10 + string.Empty + (char)13));
        Assert.AreEqual("keep  inner spaces", PasteHelper.SingleLine("keep  inner spaces"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClipboardThatAnswersAtOnce_IsUsedOnTheSpot()
    {
        await TextClipboard.Current.SetTextAsync("now");
        string? got = null;
        bool repainted = false;

        PasteHelper.Paste(text => got = text, () => repainted = true);

        Assert.AreEqual("now", got);
        Assert.IsTrue(repainted);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyOrMissingClipboardInsertsNothing()
    {
        await TextClipboard.Current.SetTextAsync(string.Empty);
        bool inserted = false;

        PasteHelper.Paste(_ => inserted = true, () => inserted = true);

        Assert.IsFalse(inserted);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASlowClipboard_IsAppliedWhenItLands_NotWaitedFor()
    {
        ITextClipboardService original = TextClipboard.Current;
        TaskCompletionSource<string?> gate = new();
        TextClipboard.Current = new SlowClipboard(gate);
        try
        {
            string? got = null;
            TaskCompletionSource repainted = new();

            PasteHelper.Paste(text => got = text, () => repainted.TrySetResult());
            Assert.IsNull(got, "the key handler did not wait");

            gate.SetResult("late");
            await repainted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual("late", got);
        }
        finally
        {
            TextClipboard.Current = original;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASlowClipboardThatFails_InsertsNothing()
    {
        ITextClipboardService original = TextClipboard.Current;
        TaskCompletionSource<string?> gate = new();
        TextClipboard.Current = new SlowClipboard(gate);
        try
        {
            bool inserted = false;
            PasteHelper.Paste(_ => inserted = true, () => inserted = true);

            gate.SetException(new InvalidOperationException("busy"));
            await Task.Delay(100);

            Assert.IsFalse(inserted);
        }
        finally
        {
            TextClipboard.Current = original;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CtrlWAndCtrlBackspaceDeleteAWord_CtrlUClears_NothingElseDoes()
    {
        Assert.IsTrue(PasteHelper.IsDeleteWordKey(Key(ConsoleKey.W, 'w', control: true)));
        Assert.IsTrue(PasteHelper.IsDeleteWordKey(Key(ConsoleKey.Backspace, control: true)));
        Assert.IsFalse(PasteHelper.IsDeleteWordKey(Key(ConsoleKey.Backspace)));
        Assert.IsFalse(PasteHelper.IsDeleteWordKey(Key(ConsoleKey.W, 'w')));
        Assert.IsFalse(PasteHelper.IsDeleteWordKey(Key(ConsoleKey.W, 'w', control: true, alt: true)));
        Assert.IsTrue(PasteHelper.IsClearKey(Key(ConsoleKey.U, 'u', control: true)));
        Assert.IsFalse(PasteHelper.IsClearKey(Key(ConsoleKey.U, 'u')));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeleteLastWord_TakesTheWordAndTheSpacesAfterIt()
    {
        Assert.AreEqual("one two ", PasteHelper.DeleteLastWord("one two three"));
        Assert.AreEqual("one two ", PasteHelper.DeleteLastWord("one two three   "));
        Assert.AreEqual(string.Empty, PasteHelper.DeleteLastWord("single"));
        Assert.AreEqual(string.Empty, PasteHelper.DeleteLastWord("   "));
        Assert.AreEqual(string.Empty, PasteHelper.DeleteLastWord(string.Empty));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeleteLastWord_TreatsALineBreakAsABoundary_AndRemovesAFinalOneAlone()
    {
        Assert.AreEqual("first" + (char)10, PasteHelper.DeleteLastWord("first" + (char)10 + "second"));
        Assert.AreEqual("first", PasteHelper.DeleteLastWord("first" + (char)10));
    }
}
