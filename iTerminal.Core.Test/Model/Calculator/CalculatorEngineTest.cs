using iTerminal.Core.Model.Calculator;

namespace iTerminal.Core.Test.Model.Calculator;

[TestClass]
public class CalculatorEngineTest
{
    #region Basic entry and arithmetic

    [TestMethod]
    public void InputDigit_BuildsUpMultiDigitNumber()
    {
        var engine = new CalculatorEngine();

        engine.InputDigit('1');
        engine.InputDigit('2');
        engine.InputDigit('3');

        Assert.AreEqual("123", engine.Display);
    }

    [TestMethod]
    public void InputDigit_ReplacesLeadingZero()
    {
        var engine = new CalculatorEngine();

        engine.InputDigit('0');
        engine.InputDigit('5');

        Assert.AreEqual("5", engine.Display);
    }

    [TestMethod]
    public void InputDecimalPoint_AddsDecimalOnce()
    {
        var engine = new CalculatorEngine();

        engine.InputDigit('1');
        engine.InputDecimalPoint();
        engine.InputDigit('5');
        engine.InputDecimalPoint();
        engine.InputDigit('9');

        Assert.AreEqual("1.59", engine.Display);
    }

    [TestMethod]
    public void InputDecimalPoint_OnNewEntry_StartsWithZero()
    {
        var engine = new CalculatorEngine();

        engine.InputDecimalPoint();
        engine.InputDigit('5');

        Assert.AreEqual("0.5", engine.Display);
    }

    [TestMethod]
    public void Add_ComputesSumOnEquals()
    {
        var engine = new CalculatorEngine();

        engine.InputDigit('2');
        engine.ApplyOperator("+");
        engine.InputDigit('3');
        engine.Evaluate();

        Assert.AreEqual("5", engine.Display);
    }

    [TestMethod]
    public void ChainedOperators_EvaluateLeftToRight()
    {
        var engine = new CalculatorEngine();

        engine.InputDigit('2');
        engine.ApplyOperator("+");
        engine.InputDigit('3');
        engine.ApplyOperator("*");
        engine.InputDigit('4');
        engine.Evaluate();

        // (2 + 3) * 4 = 20, matching the chained (non-precedence) evaluation used by simple calculators.
        Assert.AreEqual("20", engine.Display);
    }

    [TestMethod]
    public void Subtract_ProducesNegativeResult()
    {
        var engine = new CalculatorEngine();

        engine.InputDigit('3');
        engine.ApplyOperator("-");
        engine.InputDigit('5');
        engine.Evaluate();

        Assert.AreEqual("-2", engine.Display);
    }

    [TestMethod]
    public void Divide_ByZero_SetsErrorState()
    {
        var engine = new CalculatorEngine();

        engine.InputDigit('5');
        engine.ApplyOperator("/");
        engine.InputDigit('0');
        engine.Evaluate();

        Assert.IsTrue(engine.IsError);
        Assert.AreEqual("Error", engine.Display);
    }

    [TestMethod]
    public void InputDigit_AfterError_ClearsAndStartsFresh()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('5');
        engine.ApplyOperator("/");
        engine.InputDigit('0');
        engine.Evaluate();
        Assert.IsTrue(engine.IsError);

        engine.InputDigit('7');

        Assert.IsFalse(engine.IsError);
        Assert.AreEqual("7", engine.Display);
    }

    [TestMethod]
    public void Clear_ResetsDisplayAndPendingOperator()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('9');
        engine.ApplyOperator("+");

        engine.Clear();
        engine.InputDigit('4');
        engine.Evaluate();

        // no pending operator survives Clear, so Evaluate is a no-op and display stays "4"
        Assert.AreEqual("4", engine.Display);
    }

    [TestMethod]
    public void ClearEntry_KeepsPendingOperatorAndAccumulator()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('9');
        engine.ApplyOperator("+");
        engine.InputDigit('1');
        engine.InputDigit('2');

        engine.ClearEntry();
        engine.InputDigit('1');
        engine.Evaluate();

        Assert.AreEqual("10", engine.Display);
    }

    [TestMethod]
    public void Backspace_RemovesLastDigit()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('1');
        engine.InputDigit('2');
        engine.InputDigit('3');

        engine.Backspace();

        Assert.AreEqual("12", engine.Display);
    }

    [TestMethod]
    public void Backspace_ToSingleDigit_ResetsToZero()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('7');

        engine.Backspace();

        Assert.AreEqual("0", engine.Display);
    }

    [TestMethod]
    public void ToggleSign_FlipsAndUnflips()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('5');

        engine.ToggleSign();
        Assert.AreEqual("-5", engine.Display);

        engine.ToggleSign();
        Assert.AreEqual("5", engine.Display);
    }

    [TestMethod]
    public void ToggleSign_OnZero_StaysZero()
    {
        var engine = new CalculatorEngine();

        engine.ToggleSign();

        Assert.AreEqual("0", engine.Display);
    }

    [TestMethod]
    public void Percent_DividesByOneHundred()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('5');
        engine.InputDigit('0');

        engine.Percent();

        Assert.AreEqual("0.5", engine.Display);
    }

    #endregion

    #region Memory

    [TestMethod]
    public void MemoryAddAndRecall_AccumulatesAcrossEntries()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('4');
        engine.MemoryAdd();
        engine.Clear();
        engine.InputDigit('6');
        engine.MemoryAdd();
        engine.Clear();

        engine.MemoryRecall();

        Assert.AreEqual("10", engine.Display);
        Assert.AreEqual(10, engine.Memory);
    }

    [TestMethod]
    public void MemoryStore_ReplacesPreviousValue()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('4');
        engine.MemoryAdd();
        engine.Clear();
        engine.InputDigit('9');

        engine.MemoryStore();

        Assert.AreEqual(9, engine.Memory);
    }

    [TestMethod]
    public void MemorySubtract_DecreasesStoredValue()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('1');
        engine.InputDigit('0');
        engine.MemoryStore();
        engine.Clear();
        engine.InputDigit('3');

        engine.MemorySubtract();

        Assert.AreEqual(7, engine.Memory);
    }

    [TestMethod]
    public void MemoryClear_ResetsToZero()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('4');
        engine.MemoryStore();

        engine.MemoryClear();

        Assert.AreEqual(0, engine.Memory);
    }

    #endregion

    #region Scientific

    [TestMethod]
    public void ApplyUnary_Sqrt_ComputesRoot()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);
        engine.InputDigit('9');

        engine.ApplyUnary("sqrt");

        Assert.AreEqual("3", engine.Display);
    }

    [TestMethod]
    public void ApplyUnary_SqrtOfNegative_SetsError()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);
        engine.InputDigit('9');
        engine.ToggleSign();

        engine.ApplyUnary("sqrt");

        Assert.IsTrue(engine.IsError);
    }

    [TestMethod]
    public void ApplyUnary_Sin_InDegrees_ComputesExpectedValue()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);
        engine.InputDigit('9');
        engine.InputDigit('0');

        engine.ApplyUnary("sin");

        Assert.AreEqual(1.0, double.Parse(engine.Display), 1e-9);
    }

    [TestMethod]
    public void ApplyUnary_Sin_InRadians_ComputesExpectedValue()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);
        engine.AngleUnit = AngleUnit.Radians;
        engine.InputDigit('0');

        engine.ApplyUnary("sin");

        Assert.AreEqual(0.0, double.Parse(engine.Display), 1e-9);
    }

    [TestMethod]
    public void ApplyUnary_Square_ComputesExpectedValue()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);
        engine.InputDigit('5');

        engine.ApplyUnary("sqr");

        Assert.AreEqual("25", engine.Display);
    }

    [TestMethod]
    public void ApplyUnary_Factorial_ComputesExpectedValue()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);
        engine.InputDigit('5');

        engine.ApplyUnary("fact");

        Assert.AreEqual("120", engine.Display);
    }

    [TestMethod]
    public void ApplyUnary_FactorialOfNegative_SetsError()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);
        engine.InputDigit('5');
        engine.ToggleSign();

        engine.ApplyUnary("fact");

        Assert.IsTrue(engine.IsError);
    }

    [TestMethod]
    public void ApplyUnary_InverseOfZero_SetsError()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);

        engine.ApplyUnary("inv");

        Assert.IsTrue(engine.IsError);
    }

    [TestMethod]
    public void ApplyOperator_Pow_ComputesExpectedValue()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);
        engine.InputDigit('2');
        engine.ApplyOperator("pow");
        engine.InputDigit('1');
        engine.InputDigit('0');
        engine.Evaluate();

        Assert.AreEqual("1024", engine.Display);
    }

    [TestMethod]
    public void InputConstant_Pi_SetsDisplayToPi()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Scientific);

        engine.InputConstant("pi");

        Assert.AreEqual(Math.PI, double.Parse(engine.Display), 1e-9);
    }

    #endregion

    #region Programming mode

    [TestMethod]
    public void SetMode_Programming_ConvertsCurrentDecimalValueToInteger()
    {
        var engine = new CalculatorEngine();
        engine.InputDigit('4');
        engine.InputDigit('2');

        engine.SetMode(CalculatorMode.Programming);

        Assert.AreEqual("42", engine.Display);
    }

    [TestMethod]
    public void SetNumeralBase_Hexadecimal_ReformatsCurrentValue()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.InputDigit('2');
        engine.InputDigit('5');
        engine.InputDigit('5');

        engine.SetNumeralBase(NumeralBase.Hexadecimal);

        Assert.AreEqual("FF", engine.Display);
    }

    [TestMethod]
    public void SetNumeralBase_Binary_ReformatsCurrentValue()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.InputDigit('9');

        engine.SetNumeralBase(NumeralBase.Binary);

        Assert.AreEqual("1001", engine.Display);
    }

    [TestMethod]
    public void InputDigit_InvalidForBinaryBase_IsIgnored()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.SetNumeralBase(NumeralBase.Binary);

        engine.InputDigit('9');

        Assert.AreEqual("0", engine.Display);
    }

    [TestMethod]
    public void InputDigit_HexLetter_IsAcceptedInHexBase()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.SetNumeralBase(NumeralBase.Hexadecimal);

        engine.InputDigit('a');
        engine.InputDigit('5');

        Assert.AreEqual("A5", engine.Display);
    }

    [TestMethod]
    public void ApplyOperator_And_ComputesBitwiseAnd()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.InputDigit('6');
        engine.ApplyOperator("and");
        engine.InputDigit('3');
        engine.Evaluate();

        Assert.AreEqual("2", engine.Display);
    }

    [TestMethod]
    public void ApplyOperator_Or_ComputesBitwiseOr()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.InputDigit('6');
        engine.ApplyOperator("or");
        engine.InputDigit('1');
        engine.Evaluate();

        Assert.AreEqual("7", engine.Display);
    }

    [TestMethod]
    public void ApplyOperator_Xor_ComputesBitwiseXor()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.InputDigit('6');
        engine.ApplyOperator("xor");
        engine.InputDigit('3');
        engine.Evaluate();

        Assert.AreEqual("5", engine.Display);
    }

    [TestMethod]
    public void ApplyUnary_Not_InDecimalBase_ComputesBitwiseComplementOfZero()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);

        engine.ApplyUnary("not");

        Assert.AreEqual("-1", engine.Display);
    }

    [TestMethod]
    public void ApplyUnary_Not_InHexBase_ShowsTwosComplementBitPattern()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.SetNumeralBase(NumeralBase.Hexadecimal);

        engine.ApplyUnary("not");

        Assert.AreEqual("FFFFFFFFFFFFFFFF", engine.Display);
    }

    [TestMethod]
    public void ApplyOperator_ShiftLeft_ComputesExpectedValue()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.InputDigit('1');
        engine.ApplyOperator("shl");
        engine.InputDigit('4');
        engine.Evaluate();

        Assert.AreEqual("16", engine.Display);
    }

    [TestMethod]
    public void ApplyOperator_ShiftRight_ComputesExpectedValue()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.InputDigit('1');
        engine.InputDigit('6');
        engine.ApplyOperator("shr");
        engine.InputDigit('4');
        engine.Evaluate();

        Assert.AreEqual("1", engine.Display);
    }

    [TestMethod]
    public void InputDecimalPoint_InProgrammingMode_IsIgnored()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        engine.InputDigit('5');

        engine.InputDecimalPoint();

        Assert.AreEqual("5", engine.Display);
    }

    #endregion
}
