using System.Globalization;

namespace iTerminal.Core.Model.UnitConverter;

/// <summary>
/// Pure conversion state behind <see cref="Dialogs.UnitConverter.UnitConverterDialog"/>. Kept free of
/// any UI/control dependencies so category switching and the arithmetic can be unit tested directly.
/// The dialog owns two selection cursors (<see cref="FromUnitIndex"/>/<see cref="ToUnitIndex"/>) into
/// the current category's unit list and one free-text input; every mutator recomputes
/// <see cref="ResultText"/> immediately.
/// </summary>
public class UnitConverterEngine
{
    public UnitCategory Category { get; private set; } = UnitCategory.Length;
    public IReadOnlyList<UnitDefinition> Units { get; private set; } = UnitCatalog.GetUnits(UnitCategory.Length);
    public int FromUnitIndex { get; private set; }
    public int ToUnitIndex { get; private set; } = 1;
    public string InputText { get; private set; } = "1";
    public string ResultText { get; private set; } = string.Empty;

    public UnitConverterEngine() => Recompute();

    public void SetCategory(UnitCategory category)
    {
        if (Category == category)
        {
            return;
        }

        Category = category;
        Units = UnitCatalog.GetUnits(category);
        FromUnitIndex = 0;
        ToUnitIndex = Units.Count > 1 ? 1 : 0;
        Recompute();
    }

    public void SetFromUnit(int index)
    {
        if (index < 0 || index >= Units.Count || index == FromUnitIndex)
        {
            return;
        }

        FromUnitIndex = index;
        Recompute();
    }

    public void SetToUnit(int index)
    {
        if (index < 0 || index >= Units.Count || index == ToUnitIndex)
        {
            return;
        }

        ToUnitIndex = index;
        Recompute();
    }

    public void SwapUnits()
    {
        (FromUnitIndex, ToUnitIndex) = (ToUnitIndex, FromUnitIndex);
        Recompute();
    }

    public void SetInputText(string text)
    {
        text ??= string.Empty;
        if (InputText == text)
        {
            return;
        }

        InputText = text;
        Recompute();
    }

    private void Recompute()
    {
        // A decimal comma is a decimal too: "1,5" typed on a comma-decimal keyboard was "not a number".
        // Float allows no thousands separators, so the comma cannot be misread as one.
        if (!double.TryParse(InputText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && !double.TryParse(InputText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            ResultText = string.Empty;
            return;
        }

        var fromUnit = Units[FromUnitIndex];
        var toUnit = Units[ToUnitIndex];
        var result = Category == UnitCategory.Temperature
            ? ConvertTemperature(value, fromUnit.Symbol, toUnit.Symbol)
            : value * fromUnit.ToBaseFactor / toUnit.ToBaseFactor;

        ResultText = FormatResult(result);
    }

    private static double ConvertTemperature(double value, string fromSymbol, string toSymbol)
    {
        var celsius = fromSymbol switch
        {
            "F" => (value - 32) * 5.0 / 9.0,
            "K" => value - 273.15,
            _ => value
        };
        return toSymbol switch
        {
            "F" => celsius * 9.0 / 5.0 + 32,
            "K" => celsius + 273.15,
            _ => celsius
        };
    }

    private static string FormatResult(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "Error";
        }
        return value.ToString("G10", CultureInfo.InvariantCulture);
    }
}
