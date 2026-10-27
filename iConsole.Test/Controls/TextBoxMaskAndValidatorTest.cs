using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Model.Validations;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iConsole.Test.Controls;

[TestClass]
public class TextBoxMaskAndValidatorTest
{
    private readonly Mock<IBeepService> _beep = new();

    private TextBox Create(string? mask = null)
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var box = new TextBox(_beep.Object, new Mock<IDateTimeProviderService>().Object, theme.Object)
        {
            ViewSize = new Size(30, 1),
        };
        box.ScrollableModel.ViewSize = new Size(25, 1);
        box.SetFocus();
        if (mask is not null)
        {
            box.Mask = new InputMask(mask);
        }

        return box;
    }

    private static Task<bool> Press(TextBox box, ConsoleKey key, char c = ' ')
        => box.HandleKeyEventAsync(new KeyEvent(c, key, false, false, false));

    private static async Task Type(TextBox box, string text)
    {
        foreach (char c in text)
        {
            await Press(box, char.IsDigit(c) ? ConsoleKey.D0 : ConsoleKey.A, c);
        }
    }

    // ---- the mask itself ----

    [TestMethod]
    public void Format_AddsLiteralsOnlyBeforeACharacterThatFollows()
    {
        var mask = new InputMask("(000) 000-0000");

        Assert.AreEqual(string.Empty, mask.Format(""));
        Assert.AreEqual("(5", mask.Format("5"));
        Assert.AreEqual("(555", mask.Format("555"));
        Assert.AreEqual("(555) 1", mask.Format("5551"));
        Assert.AreEqual("(555) 123-4567", mask.Format("5551234567"));
        Assert.AreEqual("(___) ___-____", mask.Template);
    }

    [TestMethod]
    public void ExtractRaw_ReadsFormattedAndUnformattedText()
    {
        var mask = new InputMask("(000) 000-0000");

        Assert.AreEqual("5551234567", mask.ExtractRaw("(555) 123-4567"));
        Assert.AreEqual("5551234567", mask.ExtractRaw("5551234567"));
        Assert.AreEqual("555", mask.ExtractRaw("(555"));
        Assert.AreEqual("555", mask.ExtractRaw("a5b5c5"), "characters a slot does not take are dropped");
        Assert.AreEqual(string.Empty, mask.ExtractRaw(null));
    }

    [TestMethod]
    public void Slots_TakeTheirKindOfCharacter()
    {
        var mask = new InputMask("0L A&");

        Assert.IsTrue(mask.Accepts(0, '7'));
        Assert.IsFalse(mask.Accepts(0, 'x'));
        Assert.IsTrue(mask.Accepts(1, 'x'));
        Assert.IsFalse(mask.Accepts(1, '7'));
        Assert.IsTrue(mask.Accepts(2, '7'));
        Assert.IsTrue(mask.Accepts(2, 'x'));
        Assert.IsFalse(mask.Accepts(2, '-'));
        Assert.IsTrue(mask.Accepts(3, '-'));
        Assert.IsFalse(mask.Accepts(4, 'x'), "past the last slot");
        Assert.AreEqual(4, mask.SlotCount);
    }

    [TestMethod]
    public void CaseModifiers_AndEscapedLiterals()
    {
        var mask = new InputMask(">LL<LL|LL\\0");

        Assert.AreEqual("AB", mask.Format("ab"));
        Assert.AreEqual("ABcd", mask.Format("abCD"));
        Assert.AreEqual("ABcdEf", mask.Format("abCDEf"), "the escaped literal is only shown once something follows it");
        Assert.AreEqual('Q', mask.Normalize(0, 'q'));
        Assert.AreEqual('q', mask.Normalize(2, 'Q'));
        Assert.AreEqual('Q', mask.Normalize(4, 'Q'));
    }

    [TestMethod]
    public void IsComplete_NeedsEveryRequiredSlot_AndOptionalOnesMayBeLeftOut()
    {
        var mask = new InputMask("00-99");

        Assert.IsFalse(mask.IsComplete("1"));
        Assert.IsTrue(mask.IsComplete("12"));
        Assert.IsTrue(mask.IsComplete("123"));
        Assert.IsFalse(mask.IsComplete("1a"));
        Assert.IsFalse(mask.IsComplete("12345"));
        Assert.AreEqual(2, mask.RequiredCount);
    }

    [TestMethod]
    public void SlotsBefore_CountsOnlyCharactersTheCursorHasPassed()
    {
        var mask = new InputMask("(000) 000");

        Assert.AreEqual(0, mask.SlotsBefore("(555) 12", 0));
        Assert.AreEqual(0, mask.SlotsBefore("(555) 12", 1), "between the bracket and the first digit");
        Assert.AreEqual(1, mask.SlotsBefore("(555) 12", 2));
        Assert.AreEqual(3, mask.SlotsBefore("(555) 12", 5), "inside the literals after the third digit");
        Assert.AreEqual(5, mask.SlotsBefore("(555) 12", 8));
        Assert.AreEqual(5, mask.SlotsBefore("(555) 12", 99));
    }

    // ---- typing into a masked box ----

    [TestMethod]
    public async Task Typing_FillsSlotsAndSkipsLiterals()
    {
        TextBox box = Create("(000) 000-0000");

        await Type(box, "5551234567");

        Assert.AreEqual("(555) 123-4567", box.Text);
        Assert.AreEqual("5551234567", box.MaskedValue);
        Assert.IsTrue(box.IsMaskComplete);
        Assert.AreEqual(14, box.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task Typing_RefusesACharacterTheSlotDoesNotTake_WithABeep()
    {
        TextBox box = Create("00-LL");

        await Type(box, "1x2");

        Assert.AreEqual("12", box.Text);
        _beep.Verify(b => b.Beep(), Times.Once);
    }

    [TestMethod]
    public async Task Typing_BeyondTheLastSlotIsRefused()
    {
        TextBox box = Create("00");

        await Type(box, "123");

        Assert.AreEqual("12", box.Text);
        _beep.Verify(b => b.Beep(), Times.Once);
    }

    [TestMethod]
    public async Task Typing_InTheMiddleInsertsAndPushesLaterCharactersAlong()
    {
        TextBox box = Create("00000");
        await Type(box, "1234");
        await Press(box, ConsoleKey.Home);
        await Press(box, ConsoleKey.RightArrow);

        await Type(box, "9");

        Assert.AreEqual("19234", box.Text);
        Assert.AreEqual(2, box.ScrollableModel.CursorPosition.X);

        await Type(box, "8");
        Assert.AreEqual("19234", box.Text, "the box is full, so the extra character is refused");
        _beep.Verify(b => b.Beep(), Times.Once);
    }

    [TestMethod]
    public async Task Typing_LowerCaseInAnUpperCaseSlotIsConverted()
    {
        TextBox box = Create(">LL");

        await Type(box, "ab");

        Assert.AreEqual("AB", box.Text);
    }

    [TestMethod]
    public async Task Backspace_RemovesTheLastCharacterAndItsLiterals()
    {
        TextBox box = Create("(000) 000");
        await Type(box, "5551");

        await Press(box, ConsoleKey.Backspace);
        Assert.AreEqual("(555", box.Text);
        await Press(box, ConsoleKey.Backspace);
        Assert.AreEqual("(55", box.Text);

        await Press(box, ConsoleKey.Backspace);
        await Press(box, ConsoleKey.Backspace);
        await Press(box, ConsoleKey.Backspace);
        Assert.AreEqual(string.Empty, box.Text);
        await Press(box, ConsoleKey.Backspace);
        Assert.AreEqual(string.Empty, box.Text);
    }

    [TestMethod]
    public async Task Delete_RemovesTheSlotAfterTheCursor()
    {
        TextBox box = Create("0000");
        await Type(box, "1234");
        await Press(box, ConsoleKey.Home);

        await Press(box, ConsoleKey.Delete);

        Assert.AreEqual("234", box.Text);
        Assert.AreEqual(0, box.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public void Paste_KeepsOnlyWhatTheSlotsTake()
    {
        TextBox box = Create("(000) 000-0000");

        box.InsertText("Call (555) 123-4567 now!");

        Assert.AreEqual("(555) 123-4567", box.Text);
    }

    [TestMethod]
    public void SettingAMaskReformatsTheTextAlreadyThere()
    {
        TextBox box = Create();
        box.Text = "5551234567";

        box.Mask = new InputMask("(000) 000-0000");

        Assert.AreEqual("(555) 123-4567", box.Text);
        box.Mask = null;
        Assert.AreEqual("(555) 123-4567", box.MaskedValue);
    }

    [TestMethod]
    public async Task Draw_ShowsWhatIsStillToTypeInGrey()
    {
        TextBox box = Create("(000) 000");
        await Type(box, "55");
        var screen = new ScreenBuffer(30, 1);

        box.Draw(screen);
        box.Draw(screen);

        StringAssert.Contains(screen.Line(0), "(55_) ___");
        int hint = screen.Line(0).IndexOf('_');
        Assert.AreEqual(Color.Gray, screen.ForegroundAt(hint, 0));
        Assert.AreNotEqual(Color.Gray, screen.ForegroundAt(hint - 1, 0));
    }

    [TestMethod]
    public void ReadOnlyBox_IgnoresTyping()
    {
        TextBox box = Create("000");
        box.IsReadOnly = true;

        bool handled = Press(box, ConsoleKey.D1, '1').Result;

        Assert.IsFalse(handled && box.Text.Length > 0);
        Assert.AreEqual(string.Empty, box.Text);
    }

    // ---- validators ----

    [TestMethod]
    public void NoValidators_BoxIsValid()
    {
        TextBox box = Create();

        Assert.IsTrue(box.IsValid);
        Assert.AreEqual(string.Empty, box.ErrorMessage);
        Assert.IsNull(box.Validators);
    }

    [TestMethod]
    public void Pipeline_RunsRulesInOrderAndReportsTheFirstFailure()
    {
        TextBox box = Create();
        box.AddValidator(InputValidators.Required("Name needed"))
            .AddValidator(InputValidators.MinLength(3))
            .AddValidator(InputValidators.MaxLength(5));

        Assert.IsFalse(box.IsValid);
        Assert.AreEqual("Name needed", box.ErrorMessage);

        box.Text = "ab";
        Assert.AreEqual("At least 3 characters", box.ErrorMessage);

        box.Text = "abcd";
        Assert.IsTrue(box.IsValid);

        box.Text = "abcdefg";
        Assert.AreEqual("At most 5 characters", box.ErrorMessage);
    }

    [TestMethod]
    public void Pipeline_CanCollectEveryFailure()
    {
        var pipeline = new ValidatorPipeline(() => "x");
        pipeline.StopOnFirstError = false;
        pipeline.Add(InputValidators.MinLength(3)).Add(InputValidators.Email()).Add(_ => null);

        Assert.AreEqual(2, pipeline.Errors.Count);
        Assert.AreEqual("At least 3 characters", pipeline.ErrorMessage);
    }

    [TestMethod]
    public void Pipeline_ARuleThatThrows_CountsAsFailedAndDoesNotBreakTheBox()
    {
        TextBox box = Create();
        box.AddValidator(_ => throw new InvalidOperationException("boom"));

        Assert.IsFalse(box.IsValid);
        Assert.AreEqual("boom", box.ErrorMessage);
    }

    [TestMethod]
    public void Pipeline_ChecksAgainWhenTheValueChangesAndWhenARuleIsAdded()
    {
        int calls = 0;
        TextBox box = Create();
        box.AddValidator(text =>
        {
            calls++;
            return text == "bad" ? "no" : null;
        });

        _ = box.IsValid;
        _ = box.IsValid;
        Assert.AreEqual(1, calls, "an unchanged value is not checked again");

        box.Text = "bad";
        Assert.IsFalse(box.IsValid);
        box.Text = "good";
        Assert.IsTrue(box.IsValid);
    }

    [TestMethod]
    [DataRow("", null)]
    [DataRow("42", null)]
    [DataRow("-7", null)]
    [DataRow("4.2", "Enter a whole number")]
    [DataRow("abc", "Enter a whole number")]
    [DataRow("1000", "At most 100")]
    [DataRow("-1000", "At least -100")]
    public void Integer_RuleChecksFormatAndRange(string text, string? expected)
    {
        Assert.AreEqual(expected, InputValidators.Integer(-100, 100).Validate(text));
    }

    [TestMethod]
    [DataRow("1.5", null)]
    [DataRow("1e3", "At most 10")]
    [DataRow("NaN", "Enter a number")]
    [DataRow("x", "Enter a number")]
    public void Number_RuleChecksFormatAndRange(string text, string? expected)
    {
        Assert.AreEqual(expected, InputValidators.Number(0, 10).Validate(text));
    }

    [TestMethod]
    [DataRow("a@b.co", null)]
    [DataRow("first.last@sub.example.org", null)]
    [DataRow("a@b", "Enter an email address")]
    [DataRow("a b@c.de", "Enter an email address")]
    [DataRow("", null)]
    public void Email_RuleLooksForAnAddressShape(string text, string? expected)
    {
        Assert.AreEqual(expected, InputValidators.Email().Validate(text));
    }

    [TestMethod]
    public void PatternAndOneOf_Rules()
    {
        Assert.IsNull(InputValidators.Pattern("[a-z]+", "lower only").Validate("abc"));
        Assert.AreEqual("lower only", InputValidators.Pattern("[a-z]+", "lower only").Validate("abc1"));
        Assert.IsNull(InputValidators.OneOf(["Red", "Green"]).Validate("green"));
        Assert.AreEqual("Not one of: Red, Green", InputValidators.OneOf(["Red", "Green"]).Validate("blue"));
    }

    [TestMethod]
    public async Task Validators_SeeTheMaskedValueWithoutLiterals_AndMaskCompleteNeedsEverySlot()
    {
        TextBox box = Create("(000) 000");
        box.AddValidator(InputValidators.MaskComplete(box.Mask!, "Phone is incomplete"))
            .AddValidator(InputValidators.Integer(min: 100000));

        await Type(box, "55");
        Assert.AreEqual("Phone is incomplete", box.ErrorMessage);

        await Type(box, "5123");
        Assert.IsTrue(box.IsValid, box.ErrorMessage);
    }

    [TestMethod]
    public void InvalidBox_IsDrawnWithTheErrorColour()
    {
        TextBox box = Create();
        box.AddValidator(InputValidators.Required());
        var screen = new ScreenBuffer(30, 1);

        box.Draw(screen);

        Assert.AreEqual(new ColorScheme().Error, screen.BackgroundAt(1, 0));
    }
}
