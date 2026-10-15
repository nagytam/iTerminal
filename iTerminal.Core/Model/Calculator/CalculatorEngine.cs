using System.Globalization;

namespace iTerminal.Core.Model.Calculator;

public enum CalculatorMode { Basic, Scientific, Programming }

public enum AngleUnit { Degrees, Radians }

public enum NumeralBase { Binary = 2, Octal = 8, Decimal = 10, Hexadecimal = 16 }

/// <summary>
/// Pure calculation state machine behind <see cref="Dialogs.Calculator.CalculatorDialog"/>. Kept free of
/// any UI/control dependencies so arithmetic, scientific and programmer-mode behavior can be unit tested
/// directly. All entry/operator methods mutate <see cref="Display"/>; the dialog re-reads it after every
/// action to redraw.
/// </summary>
public class CalculatorEngine
{
    private const int MaxDigits = 16;

    private double _accumulator;
    private string? _pendingOperator;
    private bool _startNewEntry = true;

    public CalculatorMode Mode { get; private set; } = CalculatorMode.Basic;
    public AngleUnit AngleUnit { get; set; } = AngleUnit.Degrees;
    public NumeralBase NumeralBase { get; private set; } = NumeralBase.Decimal;
    public string Display { get; private set; } = "0";
    public string Expression { get; private set; } = string.Empty;
    public double Memory { get; private set; }
    public bool IsError { get; private set; }

    public void SetMode(CalculatorMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        var value = Mode == CalculatorMode.Programming ? (double)ParseInteger() : ParseDisplay();
        Mode = mode;
        _pendingOperator = null;
        _startNewEntry = true;
        IsError = false;
        Expression = string.Empty;
        _accumulator = Mode == CalculatorMode.Programming ? Math.Truncate(value) : value;
        Display = FormatValue(_accumulator);
    }

    public void SetNumeralBase(NumeralBase numeralBase)
    {
        if (Mode != CalculatorMode.Programming || NumeralBase == numeralBase)
        {
            return;
        }

        var value = ParseInteger();
        NumeralBase = numeralBase;
        Display = FormatInteger(value);
        _startNewEntry = true;
    }

    public void InputDigit(char digit)
    {
        if (IsError)
        {
            Clear();
        }

        if (!IsValidDigit(digit))
        {
            return;
        }

        digit = char.ToUpperInvariant(digit);

        if (_startNewEntry)
        {
            Display = digit.ToString();
            _startNewEntry = false;
        }
        else if (Display == "0")
        {
            Display = digit.ToString();
        }
        else if (DigitCount(Display) < MaxDigits)
        {
            Display += digit;
        }
    }

    public void InputDecimalPoint()
    {
        if (Mode == CalculatorMode.Programming)
        {
            return;
        }

        if (IsError)
        {
            Clear();
        }

        if (_startNewEntry)
        {
            Display = "0.";
            _startNewEntry = false;
        }
        else if (!Display.Contains('.'))
        {
            Display += ".";
        }
    }

    public void Backspace()
    {
        if (IsError || _startNewEntry)
        {
            return;
        }

        Display = Display.Length > 1 ? Display[..^1] : "0";
        if (Display is "-" or "")
        {
            Display = "0";
        }
    }

    public void ToggleSign()
    {
        if (IsError)
        {
            return;
        }

        if (Display.StartsWith('-'))
        {
            Display = Display[1..];
        }
        else if (Display != "0")
        {
            Display = "-" + Display;
        }
    }

    public void Percent()
    {
        if (IsError)
        {
            return;
        }

        Display = FormatValue(ParseDisplay() / 100.0);
        _startNewEntry = true;
    }

    public void Clear()
    {
        Display = Mode == CalculatorMode.Programming ? FormatInteger(0) : "0";
        _accumulator = 0;
        _pendingOperator = null;
        _startNewEntry = true;
        IsError = false;
        Expression = string.Empty;
    }

    public void ClearEntry()
    {
        if (IsError)
        {
            Clear();
            return;
        }

        Display = Mode == CalculatorMode.Programming ? FormatInteger(0) : "0";
        _startNewEntry = true;
    }

    public void ApplyOperator(string op)
    {
        if (IsError)
        {
            return;
        }

        var value = ParseDisplay();
        if (_pendingOperator is not null && !_startNewEntry)
        {
            value = Compute(_accumulator, value, _pendingOperator);
            if (IsError)
            {
                return;
            }
        }

        _accumulator = value;
        _pendingOperator = op;
        _startNewEntry = true;
        Display = FormatValue(value);
        Expression = $"{Display} {OperatorSymbol(op)}";
    }

    public void Evaluate()
    {
        if (IsError || _pendingOperator is null)
        {
            return;
        }

        var result = Compute(_accumulator, ParseDisplay(), _pendingOperator);
        if (IsError)
        {
            return;
        }

        Display = FormatValue(result);
        _accumulator = result;
        _pendingOperator = null;
        _startNewEntry = true;
        Expression = string.Empty;
    }

    public void ApplyUnary(string function)
    {
        if (IsError)
        {
            return;
        }

        var value = ParseDisplay();
        double result;
        switch (function)
        {
            case "sin": result = Math.Sin(ToRadians(value)); break;
            case "cos": result = Math.Cos(ToRadians(value)); break;
            case "tan": result = Math.Tan(ToRadians(value)); break;
            case "asin": result = FromRadians(Math.Asin(value)); break;
            case "acos": result = FromRadians(Math.Acos(value)); break;
            case "atan": result = FromRadians(Math.Atan(value)); break;
            case "sqrt":
                if (value < 0)
                {
                    SetError();
                    return;
                }
                result = Math.Sqrt(value);
                break;
            case "sqr": result = value * value; break;
            case "cube": result = value * value * value; break;
            case "log":
                if (value <= 0)
                {
                    SetError();
                    return;
                }
                result = Math.Log10(value);
                break;
            case "ln":
                if (value <= 0)
                {
                    SetError();
                    return;
                }
                result = Math.Log(value);
                break;
            case "inv":
                if (value == 0)
                {
                    SetError();
                    return;
                }
                result = 1.0 / value;
                break;
            case "exp": result = Math.Exp(value); break;
            case "fact":
                if (value < 0 || value != Math.Truncate(value) || value > 170)
                {
                    SetError();
                    return;
                }
                result = Factorial((int)value);
                break;
            case "not": result = ~(long)value; break;
            default: return;
        }

        Display = FormatValue(result);
        _startNewEntry = true;
    }

    public void InputConstant(string name)
    {
        if (IsError)
        {
            return;
        }

        double? value = name switch { "pi" => Math.PI, "e" => Math.E, _ => null };
        if (value is null)
        {
            return;
        }

        Display = FormatValue(value.Value);
        _startNewEntry = true;
    }

    public void MemoryClear() => Memory = 0;

    public void MemoryRecall()
    {
        if (IsError)
        {
            return;
        }

        Display = FormatValue(Memory);
        _startNewEntry = true;
    }

    public void MemoryStore()
    {
        if (!IsError)
        {
            Memory = ParseDisplay();
        }
    }

    public void MemoryAdd()
    {
        if (!IsError)
        {
            Memory += ParseDisplay();
        }
    }

    public void MemorySubtract()
    {
        if (!IsError)
        {
            Memory -= ParseDisplay();
        }
    }

    private double Compute(double a, double b, string op)
    {
        switch (op)
        {
            case "+": return a + b;
            case "-": return a - b;
            case "*": return a * b;
            case "/":
                if (b == 0)
                {
                    SetError();
                    return 0;
                }
                return a / b;
            case "mod":
                if (b == 0)
                {
                    SetError();
                    return 0;
                }
                return a % b;
            case "pow": return Math.Pow(a, b);
            case "and": return (long)a & (long)b;
            case "or": return (long)a | (long)b;
            case "xor": return (long)a ^ (long)b;
            case "shl": return (long)a << (int)b;
            case "shr": return (long)a >> (int)b;
            default: return b;
        }
    }

    private static string OperatorSymbol(string op) => op switch
    {
        "mod" => "%",
        "pow" => "^",
        "and" => "AND",
        "or" => "OR",
        "xor" => "XOR",
        "shl" => "<<",
        "shr" => ">>",
        _ => op
    };

    private void SetError()
    {
        IsError = true;
        Display = "Error";
        Expression = string.Empty;
        _pendingOperator = null;
    }

    private bool IsValidDigit(char digit)
    {
        if (Mode != CalculatorMode.Programming)
        {
            return digit is >= '0' and <= '9';
        }

        var upper = char.ToUpperInvariant(digit);
        return NumeralBase switch
        {
            NumeralBase.Binary => upper is '0' or '1',
            NumeralBase.Octal => upper is >= '0' and <= '7',
            NumeralBase.Decimal => upper is >= '0' and <= '9',
            NumeralBase.Hexadecimal => upper is >= '0' and <= '9' or >= 'A' and <= 'F',
            _ => false
        };
    }

    private static int DigitCount(string text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                count++;
            }
        }
        return count;
    }

    private string FormatValue(double value) =>
        Mode == CalculatorMode.Programming ? FormatInteger((long)Math.Truncate(value)) : FormatDecimal(value);

    private static string FormatDecimal(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "Error";
        }
        return value.ToString("G15", CultureInfo.InvariantCulture);
    }

    private string FormatInteger(long value) =>
        NumeralBase == NumeralBase.Decimal
            ? value.ToString(CultureInfo.InvariantCulture)
            : Convert.ToString(value, (int)NumeralBase).ToUpperInvariant();

    private double ParseDisplay()
    {
        if (Mode == CalculatorMode.Programming)
        {
            return ParseInteger();
        }
        return double.TryParse(Display, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private long ParseInteger()
    {
        var text = string.IsNullOrEmpty(Display) ? "0" : Display;
        if (NumeralBase == NumeralBase.Decimal)
        {
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        try
        {
            return Convert.ToInt64(text, (int)NumeralBase);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            return 0;
        }
    }

    private double ToRadians(double value) => AngleUnit == AngleUnit.Degrees ? value * Math.PI / 180.0 : value;

    private double FromRadians(double value) => AngleUnit == AngleUnit.Degrees ? value * 180.0 / Math.PI : value;

    private static double Factorial(int n)
    {
        double result = 1;
        for (var i = 2; i <= n; i++)
        {
            result *= i;
        }
        return result;
    }
}
