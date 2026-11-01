using System.Text;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;
using iConsole.Inputs;
using iConsole.Jobs;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using Buffer = iConsole.Buffers.Buffer;

namespace iConsole.Test.Services;

[TestClass]
public class TerminalFocusAndShapesTest
{
    private readonly TestThemeService _theme = new();

    // ---- focus service ----

    [Timeout(60_000)]
    [TestMethod]
    public void Focus_StartsFocused_AndOnlyAChangeRaisesEvents()
    {
        var service = new TerminalFocusService();
        var changes = new List<bool>();
        int regained = 0;
        service.FocusChanged += changes.Add;
        service.FocusRegained += () => regained++;

        Assert.IsTrue(service.HasFocus);
        Assert.IsFalse(service.IsReporting);

        service.Report(true);
        Assert.IsTrue(service.IsReporting, "a report, even of no change, shows the terminal sends them");
        Assert.AreEqual(0, changes.Count);

        service.Report(false);
        service.Report(false);
        service.Report(true);

        CollectionAssert.AreEqual(new[] { false, true }, changes);
        Assert.AreEqual(1, regained);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WaitForFocus_IsImmediateWhileFocused_AndEndsWhenFocusReturns()
    {
        var service = new TerminalFocusService();
        await service.WaitForFocusAsync();

        service.Report(false);
        Task waiting = service.WaitForFocusAsync();
        await Task.Delay(30);
        Assert.IsFalse(waiting.IsCompleted);

        service.Report(true);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(waiting.IsCompletedSuccessfully);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WaitForFocus_CanBeCancelled_AndEveryWaiterIsReleased()
    {
        var service = new TerminalFocusService();
        service.Report(false);
        using var cancellation = new CancellationTokenSource();
        Task cancelled = service.WaitForFocusAsync(cancellation.Token);
        Task first = service.WaitForFocusAsync();
        Task second = service.WaitForFocusAsync();

        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelled);

        service.Report(true);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASecondLoss_WaitsAgain()
    {
        var service = new TerminalFocusService();
        service.Report(false);
        service.Report(true);
        service.Report(false);

        Task waiting = service.WaitForFocusAsync();
        await Task.Delay(30);

        Assert.IsFalse(waiting.IsCompleted);
        service.Report(true);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- the reports in the input ----

    [Timeout(60_000)]
    [TestMethod]
    public void Decoder_ReadsFocusInAndOut_NotAsKeys()
    {
        var decoder = new TerminalInputUtf8Decoder();
        var reports = new List<bool>();
        var keys = new List<KeyEvent>();
        decoder.FocusReported += reports.Add;

        decoder.Append(Encoding.UTF8.GetBytes("a\u001b[Ob\u001b[Ic"));
        decoder.Drain(keys.Add, _ => { });

        CollectionAssert.AreEqual(new[] { false, true }, reports);
        Assert.AreEqual("abc", new string(keys.Select(k => k.KeyChar).ToArray()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Decoder_AFocusReportSplitAcrossReads_StillArrives()
    {
        var decoder = new TerminalInputUtf8Decoder();
        var reports = new List<bool>();
        decoder.FocusReported += reports.Add;

        decoder.Append("\u001b["u8);
        decoder.Drain(_ => { }, _ => { });
        Assert.AreEqual(0, reports.Count);
        decoder.Append("I"u8);
        decoder.Drain(_ => { }, _ => { });

        CollectionAssert.AreEqual(new[] { true }, reports);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Decoder_ArrowKeysAndSs3Keys_AreNotMistakenForFocus()
    {
        var decoder = new TerminalInputUtf8Decoder();
        var reports = new List<bool>();
        var keys = new List<KeyEvent>();
        decoder.FocusReported += reports.Add;

        decoder.Append(Encoding.UTF8.GetBytes("\u001b[A\u001bOP\u001b[H"));
        decoder.Drain(keys.Add, _ => { });

        Assert.AreEqual(0, reports.Count);
        CollectionAssert.AreEqual(
            new[] { ConsoleKey.UpArrow, ConsoleKey.F1, ConsoleKey.Home }, keys.Select(k => k.ConsoleKey).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FocusReporting_SequencesAreTheOnesTerminalsKnow()
    {
        string enable = TerminalFocusReporting.Enable;
        Assert.AreEqual("\u001b[?1004h", enable);
        string disable = TerminalFocusReporting.Disable;
        Assert.AreEqual("\u001b[?1004l", disable);
    }

    // ---- polling that stands still ----

    private sealed class CountingControl(IThemeService theme) : Control(theme)
    {
        public int Refreshes;

        public override void Refresh()
        {
            Interlocked.Increment(ref Refreshes);
        }

        public void Start() => StartAutoRefresh();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoRefresh_StandsStillInTheBackground_AndRunsAgainOnFocus()
    {
        ITerminalFocusService previous = TerminalFocus.Current;
        var focus = new TerminalFocusService();
        TerminalFocus.Current = focus;
        var control = new CountingControl(_theme);
        try
        {
            focus.Report(false);
            control.Start();
            await Task.Delay(350);
            Assert.AreEqual(0, control.Refreshes, "no repaint while the window is in the background");

            focus.Report(true);
            await Task.Delay(350);
            Assert.IsTrue(control.Refreshes > 0, "repainting resumes with focus");
        }
        finally
        {
            control.StopAutoRefresh();
            TerminalFocus.Current = previous;
        }
    }

    // ---- cursor shape ----

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(CursorShape.Default, "\u001b[0 q")]
    [DataRow(CursorShape.BlinkingBlock, "\u001b[1 q")]
    [DataRow(CursorShape.SteadyUnderline, "\u001b[4 q")]
    [DataRow(CursorShape.BlinkingBar, "\u001b[5 q")]
    [DataRow(CursorShape.SteadyBar, "\u001b[6 q")]
    public void CursorShape_SequenceIsDecscusr(CursorShape shape, string expected)
    {
        Assert.AreEqual(expected, CursorShapes.Sequence(shape));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextFieldsAskForABar_AndOtherControlsLeaveTheTerminalsShape()
    {
        var parent = new Control(_theme);
        var plain = new Control(_theme);
        var box = new TextBox(new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, _theme);
        parent.AddControl(plain);
        Assert.AreEqual(CursorShape.Default, parent.EffectiveCursorShape);

        parent.AddControl(box);
        box.IsCursorVisible = true;

        Assert.AreEqual(CursorShape.BlinkingBar, box.CursorShape);
        Assert.AreEqual(CursorShape.BlinkingBar, parent.EffectiveCursorShape, "the shape of the child that shows the cursor");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AControlsOwnShapeWinsOverItsChildren()
    {
        var parent = new Control(_theme) { CursorShape = CursorShape.SteadyBlock };
        var box = new TextBox(new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, _theme);
        parent.AddControl(box);
        box.IsCursorVisible = true;

        Assert.AreEqual(CursorShape.SteadyBlock, parent.EffectiveCursorShape);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRenderer_TellsTheDriverTheShapeWhileTheCursorShows()
    {
        var driver = new Mock<IConsoleDriver>();
        driver.SetupGet(d => d.ForegroundColor).Returns(Color.White);
        driver.SetupGet(d => d.BackgroundColor).Returns(Color.Black);
        driver.Setup(d => d.GetCursorPosition()).Returns(new Position(0, 0));
        driver.SetupGet(d => d.WindowSize).Returns(new Size(80, 24));
        var buffer = new Buffer(driver.Object);
        var job = new ConsoleBufferRendererJob(driver.Object, buffer);
        buffer.Cursor.IsCursorVisible = true;
        buffer.Cursor.CursorPosition = new Position(3, 2);
        buffer.Cursor.Shape = CursorShape.BlinkingBar;

        var update = typeof(ConsoleBufferRendererJob).GetMethod("UpdateCursor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        update.Invoke(job, [false]);

        driver.VerifySet(d => d.CursorShape = CursorShape.BlinkingBar, Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRenderer_LeavesTheShapeAloneWhileTheCursorIsHidden()
    {
        var driver = new Mock<IConsoleDriver>();
        driver.SetupGet(d => d.WindowSize).Returns(new Size(80, 24));
        var buffer = new Buffer(driver.Object);
        var job = new ConsoleBufferRendererJob(driver.Object, buffer);
        buffer.Cursor.IsCursorVisible = false;
        buffer.Cursor.Shape = CursorShape.BlinkingBar;

        var update = typeof(ConsoleBufferRendererJob).GetMethod("UpdateCursor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        update.Invoke(job, [false]);

        driver.VerifySet(d => d.CursorShape = It.IsAny<CursorShape>(), Times.Never);
    }

    // ---- pointer shape ----

    private static PointerShapeService Service(StringWriter output, bool enabled = true, ConsoleOwnership? ownership = null)
        => new(() => output, ownership, enabled);

    [Timeout(60_000)]
    [TestMethod]
    public void PointerShape_WritesOsc22OnlyWhenTheShapeChanges()
    {
        var output = new StringWriter();
        PointerShapeService service = Service(output);

        service.Set(PointerShape.Pointer);
        service.Set(PointerShape.Pointer);
        service.Set(PointerShape.Text);

        Assert.AreEqual("\u001b]22;pointer\u001b\\\u001b]22;text\u001b\\", output.ToString());
        Assert.AreEqual(PointerShape.Text, service.Shape);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(PointerShape.Default, "default")]
    [DataRow(PointerShape.Pointer, "pointer")]
    [DataRow(PointerShape.Text, "text")]
    [DataRow(PointerShape.ResizeHorizontal, "ew-resize")]
    [DataRow(PointerShape.ResizeVertical, "ns-resize")]
    [DataRow(PointerShape.NotAllowed, "not-allowed")]
    public void PointerShape_NamesAreTheCssOnes(PointerShape shape, string name)
    {
        Assert.AreEqual(name, PointerShapeService.NameOf(shape));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PointerShape_ResetAndDisposeGiveThePointerBack_Once()
    {
        var output = new StringWriter();
        PointerShapeService service = Service(output);
        service.Set(PointerShape.Pointer);
        output.GetStringBuilder().Clear();

        service.Reset();
        service.Reset();
        service.Dispose();

        Assert.AreEqual("\u001b]22;default\u001b\\", output.ToString());
        Assert.AreEqual(PointerShape.Default, service.Shape);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PointerShape_NeverSetMeansNothingToReset()
    {
        var output = new StringWriter();

        Service(output).Dispose();

        Assert.AreEqual(string.Empty, output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PointerShape_DisabledWritesNothing()
    {
        var output = new StringWriter();
        PointerShapeService service = Service(output, enabled: false);

        service.Set(PointerShape.Pointer);

        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(PointerShape.Default, service.Shape);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PointerShape_NothingIsWrittenWhileTheTerminalIsLent()
    {
        var output = new StringWriter();
        var ownership = new ConsoleOwnership();
        PointerShapeService service = Service(output, ownership: ownership);

        await ownership.LendAsync(() =>
        {
            service.Set(PointerShape.Pointer);
            return 0;
        });

        Assert.AreEqual(string.Empty, output.ToString());
        service.Set(PointerShape.Pointer);
        Assert.AreNotEqual(string.Empty, output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PointerShape_OnlyForTerminalsKnownToHonourIt_OrWhenAskedFor()
    {
        static Func<string, string?> Env(params (string Key, string Value)[] pairs)
        {
            var map = pairs.ToDictionary(p => p.Key, p => p.Value);
            return key => map.GetValueOrDefault(key);
        }

        Assert.IsTrue(PointerShapeService.DetectSupport(Env(("KITTY_WINDOW_ID", "1"))));
        Assert.IsTrue(PointerShapeService.DetectSupport(Env(("TERM", "xterm-kitty"))));
        Assert.IsTrue(PointerShapeService.DetectSupport(Env(("TERM", "foot"))));
        Assert.IsTrue(PointerShapeService.DetectSupport(Env(("TERM_PROGRAM", "WezTerm"))));
        Assert.IsTrue(PointerShapeService.DetectSupport(Env(("TERM_PROGRAM", "ghostty"))));
        Assert.IsFalse(PointerShapeService.DetectSupport(Env(("TERM", "xterm-256color"))));
        Assert.IsFalse(PointerShapeService.DetectSupport(Env()));
        Assert.IsFalse(PointerShapeService.DetectSupport(Env(("KITTY_WINDOW_ID", "1"), ("TMUX", "/tmp/tmux"))), "tmux swallows the sequence");
        Assert.IsTrue(PointerShapeService.DetectSupport(Env(("ITERMINAL_POINTER_SHAPE", "on"))));
        Assert.IsFalse(PointerShapeService.DetectSupport(Env(("ITERMINAL_POINTER_SHAPE", "off"), ("TERM", "xterm-kitty"))));
    }

    // ---- what is under the pointer ----

    private TextBox NewBox() => new(new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, _theme);

    [Timeout(60_000)]
    [TestMethod]
    public void FindPointerShape_TheInnermostControlThatAsksWins()
    {
        var root = new Control(_theme) { ViewSize = new Size(60, 20) };
        TextBox box = NewBox();
        box.Position = new Position(2, 2);
        box.ViewSize = new Size(20, 1);
        var link = new LinkLabel(_theme) { Position = new Position(2, 5), ViewSize = new Size(8, 1) };
        root.AddControl(box);
        root.AddControl(link);

        Assert.AreEqual(PointerShape.Text, Control.FindPointerShape(root, new Position(5, 2)));
        Assert.AreEqual(PointerShape.Pointer, Control.FindPointerShape(root, new Position(4, 5)));
        Assert.AreEqual(PointerShape.Default, Control.FindPointerShape(root, new Position(40, 15)));
        Assert.AreEqual(PointerShape.Default, Control.FindPointerShape(null, new Position(0, 0)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FindPointerShape_AHiddenControlIsSkipped()
    {
        var root = new Control(_theme) { ViewSize = new Size(60, 20) };
        TextBox box = NewBox();
        box.Position = new Position(2, 2);
        box.ViewSize = new Size(20, 1);
        root.AddControl(box);
        box.IsVisible = false;

        Assert.AreEqual(PointerShape.Default, Control.FindPointerShape(root, new Position(5, 2)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FindPointerShape_OnlyTheDividerOfASplitterIsADoubleArrow()
    {
        var splitter = new Splitter(_theme)
        {
            Orientation = Orientation.Horizontal,
            ViewSize = new Size(21, 10),
            Position = new Position(0, 0),
        };
        splitter.SetFirst(new Control(_theme));
        splitter.SetSecond(new Control(_theme));
        splitter.SetFirstSize(10);

        Assert.AreEqual(PointerShape.ResizeHorizontal, Control.FindPointerShape(splitter, new Position(10, 4)));
        Assert.AreEqual(PointerShape.Default, Control.FindPointerShape(splitter, new Position(3, 4)));

        splitter.Orientation = Orientation.Vertical;
        splitter.SetFirstSize(4);
        Assert.AreEqual(PointerShape.ResizeVertical, Control.FindPointerShape(splitter, new Position(3, 4)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToolTipHost_SetsThePointerShapeFromWhatIsUnderThePointer()
    {
        var tips = new ToolTipService();
        var shapes = new Mock<IPointerShapeService>();
        var root = new Control(_theme) { ViewSize = new Size(60, 20) };
        TextBox box = NewBox();
        box.Position = new Position(2, 2);
        box.ViewSize = new Size(20, 1);
        root.AddControl(box);
        var host = new ToolTipHost(tips, new VirtualTimeProvider(), _theme, shapes.Object);
        root.AddControl(host);
        host.OnInitializeLayout();

        tips.ReportPointer(new Position(5, 2));
        host.Synchronize();
        shapes.Verify(s => s.Set(PointerShape.Text), Times.Once);

        tips.ReportPointer(new Position(40, 15));
        host.Synchronize();
        shapes.Verify(s => s.Set(PointerShape.Default), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToolTipHost_WorksWithoutAPointerShapeService()
    {
        var tips = new ToolTipService();
        var root = new Control(_theme) { ViewSize = new Size(60, 20) };
        var host = new ToolTipHost(tips, new VirtualTimeProvider(), _theme);
        root.AddControl(host);
        host.OnInitializeLayout();

        tips.ReportPointer(new Position(5, 2));
        host.Synchronize();
    }
}
