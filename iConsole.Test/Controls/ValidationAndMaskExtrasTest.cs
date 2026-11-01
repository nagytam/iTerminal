using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Model.Validations;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ValidationAndMaskExtrasTest
{
    // ---- async validators ----

    private static ValidatorPipeline Pipeline(Func<string> text, TimeSpan? debounce = null)
    {
        return new ValidatorPipeline(text) { AsyncDebounce = debounce ?? TimeSpan.Zero };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnAsyncRule_IsPendingThenLandsItsAnswer()
    {
        string text = "taken";
        var release = new TaskCompletionSource();
        ValidatorPipeline pipeline = Pipeline(() => text)
            .AddAsync(async (value, token) =>
            {
                await release.Task.WaitAsync(token);
                return value == "taken" ? "That name is taken" : null;
            });

        Assert.IsTrue(pipeline.IsValid, "while it waits, the value counts as valid");
        Assert.IsTrue(pipeline.IsPending);
        long before = pipeline.Version;

        release.SetResult();
        await pipeline.WaitForPendingAsync();

        Assert.IsFalse(pipeline.IsPending);
        Assert.IsFalse(pipeline.IsValid);
        Assert.AreEqual("That name is taken", pipeline.ErrorMessage);
        Assert.IsTrue(pipeline.Version > before);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AValueThatChangesMeanwhile_CancelsTheCheck_AndTheStaleAnswerIsDropped()
    {
        string text = "first";
        var started = new List<string>();
        var gates = new Dictionary<string, TaskCompletionSource>
        {
            ["first"] = new(),
            ["second"] = new(),
        };
        ValidatorPipeline pipeline = Pipeline(() => text)
            .AddAsync(async (value, token) =>
            {
                lock (started)
                {
                    started.Add(value);
                }

                await gates[value].Task.WaitAsync(token);
                return "bad: " + value;
            });

        _ = pipeline.IsValid;
        await Task.Delay(50);
        text = "second";
        _ = pipeline.IsValid;
        await Task.Delay(50);

        gates["first"].TrySetResult();
        gates["second"].TrySetResult();
        await pipeline.WaitForPendingAsync();

        Assert.AreEqual("bad: second", pipeline.ErrorMessage);
        CollectionAssert.Contains(started, "second");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheAsyncRulesWaitForTheSynchronousOnes_AndSkipAnEmptyValue()
    {
        string text = "x";
        int asked = 0;
        ValidatorPipeline pipeline = Pipeline(() => text)
            .Add(InputValidators.MinLength(3))
            .AddAsync((_, _) =>
            {
                Interlocked.Increment(ref asked);
                return Task.FromResult<string?>(null);
            });

        Assert.IsFalse(pipeline.IsValid);
        await pipeline.WaitForPendingAsync();
        Assert.AreEqual(0, asked, "a value that fails a quick rule is not sent to the server");

        text = string.Empty;
        _ = pipeline.IsValid;
        await pipeline.WaitForPendingAsync();
        Assert.AreEqual(0, asked, "nor is an empty one");

        text = "long enough";
        _ = pipeline.IsValid;
        await pipeline.WaitForPendingAsync();
        Assert.AreEqual(1, asked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnAsyncRuleThatThrows_CountsAsFailed()
    {
        ValidatorPipeline pipeline = Pipeline(() => "value")
            .AddAsync((_, _) => throw new InvalidOperationException("server down"));

        _ = pipeline.IsValid;
        await pipeline.WaitForPendingAsync();

        Assert.AreEqual("server down", pipeline.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheDebounce_HoldsTheQuestionBackUntilTheValueHasRested()
    {
        string text = "a";
        int asked = 0;
        ValidatorPipeline pipeline = Pipeline(() => text, TimeSpan.FromMilliseconds(150))
            .AddAsync((_, _) =>
            {
                Interlocked.Increment(ref asked);
                return Task.FromResult<string?>(null);
            });

        _ = pipeline.IsValid;
        await Task.Delay(40);
        text = "ab";
        _ = pipeline.IsValid;
        await Task.Delay(40);
        text = "abc";
        _ = pipeline.IsValid;
        Assert.AreEqual(0, asked);

        await pipeline.WaitForPendingAsync();

        Assert.AreEqual(1, asked, "only the value that rested was asked about");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CompletedIsRaised_WhenAnAnswerLands()
    {
        var landed = new TaskCompletionSource();
        ValidatorPipeline pipeline = Pipeline(() => "v").AddAsync((_, _) => Task.FromResult<string?>("no"));
        pipeline.Completed += () => landed.TrySetResult();

        _ = pipeline.IsValid;

        await landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("no", pipeline.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATextBox_ShowsAnAsyncAnswerAfterItsNextIdlePass()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var box = new TextBox(new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, theme.Object)
        {
            ViewSize = new Size(30, 1),
        };
        box.AddAsyncValidator((value, _) => Task.FromResult<string?>(value == "admin" ? "Reserved name" : null));
        box.Validators!.AsyncDebounce = TimeSpan.Zero;

        box.Text = "admin";
        Assert.IsTrue(box.IsValid);
        Assert.IsTrue(box.IsValidating);
        await box.Validators.WaitForPendingAsync();
        box.HasStateChanged = false;
        box.OnIdle();

        Assert.IsTrue(box.HasStateChanged, "the answer arrived on another thread, so the idle pass repaints");
        Assert.IsFalse(box.IsValid);
        Assert.AreEqual("Reserved name", box.ErrorMessage);
        Assert.IsFalse(box.IsValidating);
    }

    // ---- mask extras ----

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("0{3}", "000")]
    [DataRow("L{2}-0{2}", "LL-00")]
    [DataRow("0{2,4}", "0099")]
    [DataRow("L{1,3}", "L??")]
    [DataRow("%(00-){3}", "00-00-00-")]
    [DataRow("%(0000 ){1,3}", "0000 9999 9999 ")]
    [DataRow("A{0}0", "0")]
    [DataRow("(000) 000", "(000) 000")]
    [DataRow("\\0{2}", "\\0\\0")]
    [DataRow("a{x}", "a{x}")]
    [DataRow("{3}", "{3}")]
    public void Quantifiers_AreWrittenOut(string pattern, string expanded)
    {
        Assert.AreEqual(expanded, new InputMask(pattern).ExpandedPattern);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AVariableLengthPart_NeedsOnlyItsMinimum()
    {
        var mask = new InputMask("0{2,4}");

        Assert.AreEqual(4, mask.SlotCount);
        Assert.AreEqual(2, mask.RequiredCount);
        Assert.IsFalse(mask.IsComplete("1"));
        Assert.IsTrue(mask.IsComplete("12"));
        Assert.IsTrue(mask.IsComplete("1234"));
        Assert.IsFalse(mask.IsValidRaw("12345"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARepeatedGroup_FormatsEveryGroupAndShowsSeparatorsOnlyWhenSomethingFollows()
    {
        var mask = new InputMask("%(00-){2}00");

        Assert.AreEqual("12", mask.Format("12"));
        Assert.AreEqual("12-3", mask.Format("123"));
        Assert.AreEqual("12-34-56", mask.Format("123456"));
        Assert.AreEqual("123456", mask.ExtractRaw("12-34-56"));
        Assert.AreEqual(6, mask.SlotCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnyOptionalCharacter_AcceptsAnythingButMayBeLeftOut()
    {
        var mask = new InputMask("0~~");

        Assert.IsTrue(mask.IsComplete("7"));
        Assert.IsTrue(mask.IsComplete("7-x"));
        Assert.IsTrue(mask.Accepts(1, '-'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARunawayQuantifier_IsHeldToTheLimits()
    {
        var huge = new InputMask("0{999999}");
        var nested = new InputMask("%(0{64}){64}");

        Assert.AreEqual(InputMask.MaxRepeat, huge.SlotCount);
        Assert.IsTrue(nested.ExpandedPattern.Length <= InputMask.MaxExpandedLength);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATextBox_TypesIntoAVariableLengthGroupMask()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var box = new TextBox(new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, theme.Object)
        {
            ViewSize = new Size(40, 1),
        };
        box.ScrollableModel.ViewSize = new Size(30, 1);
        box.SetFocus();
        box.Mask = new InputMask("%(0000 ){1,3}");

        foreach (char c in "123456789")
        {
            await box.HandleKeyEventAsync(new KeyEvent(c, ConsoleKey.D0, false, false, false));
        }

        Assert.AreEqual("1234 5678 9", box.Text);
        Assert.AreEqual("123456789", box.MaskedValue);
        Assert.IsFalse(box.IsMaskComplete is false, "one full group is enough");
    }
}
