using iConsole.Services;
using iTerminal.Core.Model.Calculator;

namespace iTerminal.Core.Dialogs.Calculator;

public sealed class CalculatorProgrammingTabPage : CalculatorGridTabPage
{
    protected override int Columns => 5;

    public CalculatorProgrammingTabPage(CalculatorEngine engine, Action onChanged, IThemeService themeService)
        : base("Programming", engine, onChanged, themeService)
    {
        AddActionButton(() => BaseLabel("BIN"), () => Engine.SetNumeralBase(NumeralBase.Binary));
        AddActionButton(() => BaseLabel("OCT"), () => Engine.SetNumeralBase(NumeralBase.Octal));
        AddActionButton(() => BaseLabel("DEC"), () => Engine.SetNumeralBase(NumeralBase.Decimal));
        AddActionButton(() => BaseLabel("HEX"), () => Engine.SetNumeralBase(NumeralBase.Hexadecimal));
        AddActionButton("AC", Engine.Clear);

        AddActionButton("NOT", () => Engine.ApplyUnary("not"));
        AddActionButton("AND", () => Engine.ApplyOperator("and"));
        AddActionButton("OR", () => Engine.ApplyOperator("or"));
        AddActionButton("XOR", () => Engine.ApplyOperator("xor"));
        AddActionButton("CE", Engine.ClearEntry);

        AddActionButton("<<", () => Engine.ApplyOperator("shl"));
        AddActionButton(">>", () => Engine.ApplyOperator("shr"));
        AddActionButton("MOD", () => Engine.ApplyOperator("mod"));
        AddActionButton("Back", Engine.Backspace);
        AddActionButton("/", () => Engine.ApplyOperator("/"));

        AddActionButton("A", () => Engine.InputDigit('A'), () => Engine.NumeralBase == NumeralBase.Hexadecimal);
        AddActionButton("B", () => Engine.InputDigit('B'), () => Engine.NumeralBase == NumeralBase.Hexadecimal);
        AddActionButton("C", () => Engine.InputDigit('C'), () => Engine.NumeralBase == NumeralBase.Hexadecimal);
        AddActionButton("D", () => Engine.InputDigit('D'), () => Engine.NumeralBase == NumeralBase.Hexadecimal);
        AddActionButton("*", () => Engine.ApplyOperator("*"));

        AddActionButton("7", () => Engine.InputDigit('7'), () => Engine.NumeralBase != NumeralBase.Binary);
        AddActionButton("8", () => Engine.InputDigit('8'), () => Engine.NumeralBase is NumeralBase.Decimal or NumeralBase.Hexadecimal);
        AddActionButton("9", () => Engine.InputDigit('9'), () => Engine.NumeralBase is NumeralBase.Decimal or NumeralBase.Hexadecimal);
        AddActionButton("E", () => Engine.InputDigit('E'), () => Engine.NumeralBase == NumeralBase.Hexadecimal);
        AddActionButton("-", () => Engine.ApplyOperator("-"));

        AddActionButton("4", () => Engine.InputDigit('4'), () => Engine.NumeralBase != NumeralBase.Binary);
        AddActionButton("5", () => Engine.InputDigit('5'), () => Engine.NumeralBase != NumeralBase.Binary);
        AddActionButton("6", () => Engine.InputDigit('6'), () => Engine.NumeralBase != NumeralBase.Binary);
        AddActionButton("F", () => Engine.InputDigit('F'), () => Engine.NumeralBase == NumeralBase.Hexadecimal);
        AddActionButton("+", () => Engine.ApplyOperator("+"));

        AddActionButton("1", () => Engine.InputDigit('1'));
        AddActionButton("2", () => Engine.InputDigit('2'), () => Engine.NumeralBase != NumeralBase.Binary);
        AddActionButton("3", () => Engine.InputDigit('3'), () => Engine.NumeralBase != NumeralBase.Binary);
        AddActionButton("0", () => Engine.InputDigit('0'));
        AddActionButton("=", Engine.Evaluate);
    }

    private string BaseLabel(string label)
    {
        var isActive = label switch
        {
            "BIN" => Engine.NumeralBase == NumeralBase.Binary,
            "OCT" => Engine.NumeralBase == NumeralBase.Octal,
            "DEC" => Engine.NumeralBase == NumeralBase.Decimal,
            "HEX" => Engine.NumeralBase == NumeralBase.Hexadecimal,
            _ => false
        };
        return isActive ? $"[{label}]" : label;
    }
}
