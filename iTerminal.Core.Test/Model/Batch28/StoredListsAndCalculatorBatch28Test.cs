using iTerminal.Core.Model.Calculator;
using iTerminal.Core.Model.Calendar;
using iTerminal.Core.Model.Mail;
using iTerminal.Core.Model.ToDo;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Remote;
using iTerminal.Core.Services.Web;
using Moq;

namespace iTerminal.Core.Test.Model.Batch28;

/// <summary>A stored list with a null entry must open, and the calculator must say "Error" for results it cannot show.</summary>
[TestClass]
public class StoredListsAndCalculatorBatch28Test
{
    private static ISettingsService SettingsHolding(string json)
    {
        Mock<ISettingsService> settings = new();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns(json);
        return settings.Object;
    }

    [TestMethod]
    public void MeetingStore_NullEntry_IsSkipped()
    {
        MeetingStore store = new(SettingsHolding("[null]"));

        Assert.AreEqual(0, store.On(new DateOnly(2026, 10, 26)).Count);
    }

    [TestMethod]
    public void MailAccountStore_NullEntry_IsSkipped()
    {
        MailAccountStore store = new(SettingsHolding("[null]"));

        Assert.AreEqual(0, store.Accounts.Count);
    }

    [TestMethod]
    public void ToDoStore_NullEntry_IsSkipped()
    {
        ToDoStore store = new(SettingsHolding("[null]"));

        Assert.AreEqual(0, store.Visible(showCompleted: true).Count);
    }

    [TestMethod]
    public void NetworkProfileService_NullEntry_IsSkipped()
    {
        NetworkProfileService service = new(SettingsHolding("[null]"));

        Assert.AreEqual(0, service.GetAll().Count);
    }

    [TestMethod]
    public void RemoteConnectionService_NullEntry_NoLongerThrowsWhileSorting()
    {
        RemoteConnectionService service = new(SettingsHolding("[null]"));

        Assert.AreEqual(0, service.GetAll().Count);
    }

    [TestMethod]
    public void WebBrowserStore_NullEntry_IsSkipped()
    {
        WebBrowserStore store = new(SettingsHolding("[null]"));

        Assert.AreEqual(0, store.History.Count);
        Assert.IsFalse(store.IsBookmarked("https://a.test/"));
    }

    private static CalculatorEngine Enter(string digits)
    {
        CalculatorEngine engine = new();
        foreach (char digit in digits)
        {
            engine.InputDigit(digit);
        }

        return engine;
    }

    [TestMethod]
    public void Calculator_ZeroToANegativePower_IsAnError_NotAnErrorLookingNumber()
    {
        CalculatorEngine engine = Enter("0");
        engine.ApplyOperator("pow");
        engine.InputDigit('1');
        engine.ToggleSign();
        engine.Evaluate();

        Assert.IsTrue(engine.IsError);
        Assert.AreEqual("Error", engine.Display);
    }

    [TestMethod]
    public void Calculator_ExpOfAHugeNumber_IsAnError()
    {
        CalculatorEngine engine = Enter("1000");
        engine.ApplyUnary("exp");

        Assert.IsTrue(engine.IsError);
    }

    [TestMethod]
    public void Calculator_Programming_ResultPastSixtyFourBits_IsAnError()
    {
        CalculatorEngine engine = new();
        engine.SetMode(CalculatorMode.Programming);
        foreach (char digit in "9999999999999999")
        {
            engine.InputDigit(digit);
        }

        engine.ApplyOperator("*");
        foreach (char digit in "9999999999999999")
        {
            engine.InputDigit(digit);
        }

        engine.Evaluate();

        Assert.IsTrue(engine.IsError);
    }

    [TestMethod]
    public void Calculator_Programming_ShiftByMoreThanSixtyThree_ShiftsEverythingOut()
    {
        CalculatorEngine engine = new();
        engine.SetMode(CalculatorMode.Programming);
        engine.InputDigit('1');
        engine.ApplyOperator("shl");
        foreach (char digit in "64")
        {
            engine.InputDigit(digit);
        }

        engine.Evaluate();

        Assert.IsFalse(engine.IsError);
        Assert.AreEqual("0", engine.Display);
    }

    [TestMethod]
    public void Calculator_SineOf180Degrees_IsZero()
    {
        CalculatorEngine engine = Enter("180");
        engine.ApplyUnary("sin");

        Assert.AreEqual("0", engine.Display);
    }
}
