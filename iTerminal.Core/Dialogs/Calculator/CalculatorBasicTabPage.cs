using iConsole.Services;
using iTerminal.Core.Model.Calculator;

namespace iTerminal.Core.Dialogs.Calculator;

public sealed class CalculatorBasicTabPage : CalculatorGridTabPage
{
    protected override int Columns => 4;

    public CalculatorBasicTabPage(CalculatorEngine engine, Action onChanged, IThemeService themeService)
        : base("Basic", engine, onChanged, themeService)
    {
        AddActionButton("MC", Engine.MemoryClear);
        AddActionButton("MR", Engine.MemoryRecall);
        AddActionButton("M+", Engine.MemoryAdd);
        AddActionButton("M-", Engine.MemorySubtract);

        AddActionButton("AC", Engine.Clear);
        AddActionButton("CE", Engine.ClearEntry);
        AddActionButton("Back", Engine.Backspace);
        AddActionButton("/", () => Engine.ApplyOperator("/"));

        AddActionButton("7", () => Engine.InputDigit('7'));
        AddActionButton("8", () => Engine.InputDigit('8'));
        AddActionButton("9", () => Engine.InputDigit('9'));
        AddActionButton("*", () => Engine.ApplyOperator("*"));

        AddActionButton("4", () => Engine.InputDigit('4'));
        AddActionButton("5", () => Engine.InputDigit('5'));
        AddActionButton("6", () => Engine.InputDigit('6'));
        AddActionButton("-", () => Engine.ApplyOperator("-"));

        AddActionButton("1", () => Engine.InputDigit('1'));
        AddActionButton("2", () => Engine.InputDigit('2'));
        AddActionButton("3", () => Engine.InputDigit('3'));
        AddActionButton("+", () => Engine.ApplyOperator("+"));

        AddActionButton("+/-", Engine.ToggleSign);
        AddActionButton("0", () => Engine.InputDigit('0'));
        AddActionButton(".", Engine.InputDecimalPoint);
        AddActionButton("=", Engine.Evaluate);
    }
}
