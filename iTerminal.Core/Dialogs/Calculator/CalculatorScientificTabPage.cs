using iConsole.Services;
using iTerminal.Core.Model.Calculator;

namespace iTerminal.Core.Dialogs.Calculator;

public sealed class CalculatorScientificTabPage : CalculatorGridTabPage
{
    protected override int Columns => 5;

    public CalculatorScientificTabPage(CalculatorEngine engine, Action onChanged, IThemeService themeService)
        : base("Scientific", engine, onChanged, themeService)
    {
        AddActionButton("MC", Engine.MemoryClear);
        AddActionButton("MR", Engine.MemoryRecall);
        AddActionButton("M+", Engine.MemoryAdd);
        AddActionButton("M-", Engine.MemorySubtract);
        AddActionButton("MS", Engine.MemoryStore);

        AddActionButton(() => Engine.AngleUnit == AngleUnit.Degrees ? "Deg" : "Rad", ToggleAngleUnit);
        AddActionButton("Pi", () => Engine.InputConstant("pi"));
        AddActionButton("e", () => Engine.InputConstant("e"));
        AddActionButton("AC", Engine.Clear);
        AddActionButton("CE", Engine.ClearEntry);

        AddActionButton("sin", () => Engine.ApplyUnary("sin"));
        AddActionButton("cos", () => Engine.ApplyUnary("cos"));
        AddActionButton("tan", () => Engine.ApplyUnary("tan"));
        AddActionButton("x^2", () => Engine.ApplyUnary("sqr"));
        AddActionButton("sqrt", () => Engine.ApplyUnary("sqrt"));

        AddActionButton("log", () => Engine.ApplyUnary("log"));
        AddActionButton("ln", () => Engine.ApplyUnary("ln"));
        AddActionButton("1/x", () => Engine.ApplyUnary("inv"));
        AddActionButton("x^3", () => Engine.ApplyUnary("cube"));
        AddActionButton("n!", () => Engine.ApplyUnary("fact"));

        AddActionButton("7", () => Engine.InputDigit('7'));
        AddActionButton("8", () => Engine.InputDigit('8'));
        AddActionButton("9", () => Engine.InputDigit('9'));
        AddActionButton("/", () => Engine.ApplyOperator("/"));
        AddActionButton("x^y", () => Engine.ApplyOperator("pow"));

        AddActionButton("4", () => Engine.InputDigit('4'));
        AddActionButton("5", () => Engine.InputDigit('5'));
        AddActionButton("6", () => Engine.InputDigit('6'));
        AddActionButton("*", () => Engine.ApplyOperator("*"));
        AddActionButton("%", Engine.Percent);

        AddActionButton("1", () => Engine.InputDigit('1'));
        AddActionButton("2", () => Engine.InputDigit('2'));
        AddActionButton("3", () => Engine.InputDigit('3'));
        AddActionButton("-", () => Engine.ApplyOperator("-"));
        AddActionButton("Back", Engine.Backspace);

        AddActionButton("+/-", Engine.ToggleSign);
        AddActionButton("0", () => Engine.InputDigit('0'));
        AddActionButton(".", Engine.InputDecimalPoint);
        AddActionButton("+", () => Engine.ApplyOperator("+"));
        AddActionButton("=", Engine.Evaluate);
    }

    private void ToggleAngleUnit() =>
        Engine.AngleUnit = Engine.AngleUnit == AngleUnit.Degrees ? AngleUnit.Radians : AngleUnit.Degrees;
}
