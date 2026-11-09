using iConsole.Localization;
namespace iTerminal.Core.Model.UnitConverter;

public enum UnitCategory
{
    Length,
    Area,
    Volume,
    Mass,
    Temperature,
    Speed,
    Time,
    Pressure,
    Energy,
    Power,
    Angle,
    DigitalStorage
}

/// <summary>
/// One convertible unit within a <see cref="UnitCategory"/>. <see cref="ToBaseFactor"/> is the
/// multiplier that converts a value in this unit to the category's base unit (meter, liter,
/// kilogram, second, ...); ignored for <see cref="UnitCategory.Temperature"/>, which uses
/// dedicated offset formulas instead since it isn't a simple linear scale.
/// </summary>
public sealed record UnitDefinition(string Name, string Symbol, double ToBaseFactor);

public static class UnitCatalog
{
    public static string GetCategoryName(UnitCategory category) => category switch
    {
        UnitCategory.Length => UiText.Get("UnitCat.Length", "Length"),
        UnitCategory.Area => UiText.Get("UnitCat.Area", "Area"),
        UnitCategory.Volume => UiText.Get("UnitCat.Volume", "Volume"),
        UnitCategory.Mass => UiText.Get("UnitCat.Mass", "Mass"),
        UnitCategory.Temperature => UiText.Get("UnitCat.Temperature", "Temperature"),
        UnitCategory.Speed => UiText.Get("UnitCat.Speed", "Speed"),
        UnitCategory.Time => UiText.Get("UnitCat.Time", "Time"),
        UnitCategory.Pressure => UiText.Get("UnitCat.Pressure", "Pressure"),
        UnitCategory.Energy => UiText.Get("UnitCat.Energy", "Energy"),
        UnitCategory.Power => UiText.Get("UnitCat.Power", "Power"),
        UnitCategory.Angle => UiText.Get("UnitCat.Angle", "Angle"),
        UnitCategory.DigitalStorage => UiText.Get("UnitCat.DigitalStorage", "Digital Storage"),
        _ => category.ToString()
    };

    public static IReadOnlyList<UnitDefinition> GetUnits(UnitCategory category) => category switch
    {
        UnitCategory.Length =>
        [
            new UnitDefinition(UiText.Get("Unit.Millimeter", "Millimeter"), "mm", 0.001),
            new UnitDefinition(UiText.Get("Unit.Centimeter", "Centimeter"), "cm", 0.01),
            new UnitDefinition(UiText.Get("Unit.Meter", "Meter"), "m", 1),
            new UnitDefinition(UiText.Get("Unit.Kilometer", "Kilometer"), "km", 1000),
            new UnitDefinition(UiText.Get("Unit.Inch", "Inch"), "in", 0.0254),
            new UnitDefinition(UiText.Get("Unit.Foot", "Foot"), "ft", 0.3048),
            new UnitDefinition(UiText.Get("Unit.Yard", "Yard"), "yd", 0.9144),
            new UnitDefinition(UiText.Get("Unit.Mile", "Mile"), "mi", 1609.344),
            new UnitDefinition(UiText.Get("Unit.NauticalMile", "Nautical Mile"), "nmi", 1852),
        ],
        UnitCategory.Area =>
        [
            new UnitDefinition(UiText.Get("Unit.SquareMillimeter", "Square Millimeter"), "mm2", 0.000001),
            new UnitDefinition(UiText.Get("Unit.SquareCentimeter", "Square Centimeter"), "cm2", 0.0001),
            new UnitDefinition(UiText.Get("Unit.SquareMeter", "Square Meter"), "m2", 1),
            new UnitDefinition(UiText.Get("Unit.Hectare", "Hectare"), "ha", 10000),
            new UnitDefinition(UiText.Get("Unit.SquareKilometer", "Square Kilometer"), "km2", 1000000),
            new UnitDefinition(UiText.Get("Unit.SquareInch", "Square Inch"), "in2", 0.00064516),
            new UnitDefinition(UiText.Get("Unit.SquareFoot", "Square Foot"), "ft2", 0.09290304),
            new UnitDefinition(UiText.Get("Unit.SquareYard", "Square Yard"), "yd2", 0.83612736),
            new UnitDefinition(UiText.Get("Unit.Acre", "Acre"), "ac", 4046.8564224),
            new UnitDefinition(UiText.Get("Unit.SquareMile", "Square Mile"), "mi2", 2589988.110336),
        ],
        UnitCategory.Volume =>
        [
            new UnitDefinition(UiText.Get("Unit.Milliliter", "Milliliter"), "mL", 0.001),
            new UnitDefinition(UiText.Get("Unit.Liter", "Liter"), "L", 1),
            new UnitDefinition(UiText.Get("Unit.CubicMeter", "Cubic Meter"), "m3", 1000),
            new UnitDefinition(UiText.Get("Unit.Teaspoon", "Teaspoon"), "tsp", 0.00492892),
            new UnitDefinition(UiText.Get("Unit.Tablespoon", "Tablespoon"), "tbsp", 0.0147868),
            new UnitDefinition(UiText.Get("Unit.FluidOunce", "Fluid Ounce"), "fl oz", 0.0295735),
            new UnitDefinition(UiText.Get("Unit.Cup", "Cup"), "cup", 0.236588),
            new UnitDefinition(UiText.Get("Unit.Pint", "Pint"), "pt", 0.473176),
            new UnitDefinition(UiText.Get("Unit.Quart", "Quart"), "qt", 0.946353),
            new UnitDefinition(UiText.Get("Unit.Gallon", "Gallon"), "gal", 3.78541),
            new UnitDefinition(UiText.Get("Unit.CubicInch", "Cubic Inch"), "in3", 0.0163871),
            new UnitDefinition(UiText.Get("Unit.CubicFoot", "Cubic Foot"), "ft3", 28.3168),
        ],
        UnitCategory.Mass =>
        [
            new UnitDefinition(UiText.Get("Unit.Milligram", "Milligram"), "mg", 0.000001),
            new UnitDefinition(UiText.Get("Unit.Gram", "Gram"), "g", 0.001),
            new UnitDefinition(UiText.Get("Unit.Kilogram", "Kilogram"), "kg", 1),
            new UnitDefinition(UiText.Get("Unit.MetricTon", "Metric Ton"), "t", 1000),
            new UnitDefinition(UiText.Get("Unit.Ounce", "Ounce"), "oz", 0.0283495),
            new UnitDefinition(UiText.Get("Unit.Pound", "Pound"), "lb", 0.453592),
            new UnitDefinition(UiText.Get("Unit.Stone", "Stone"), "st", 6.35029),
            new UnitDefinition(UiText.Get("Unit.ShortTon", "Short Ton"), "ton", 907.185),
            new UnitDefinition(UiText.Get("Unit.LongTon", "Long Ton"), "lton", 1016.05),
        ],
        UnitCategory.Temperature =>
        [
            new UnitDefinition(UiText.Get("Unit.Celsius", "Celsius"), "C", 1),
            new UnitDefinition(UiText.Get("Unit.Fahrenheit", "Fahrenheit"), "F", 1),
            new UnitDefinition(UiText.Get("Unit.Kelvin", "Kelvin"), "K", 1),
        ],
        UnitCategory.Speed =>
        [
            new UnitDefinition(UiText.Get("Unit.MeterSecond", "Meter/Second"), "m/s", 1),
            new UnitDefinition(UiText.Get("Unit.KilometerHour", "Kilometer/Hour"), "km/h", 0.277778),
            new UnitDefinition(UiText.Get("Unit.MileHour", "Mile/Hour"), "mph", 0.44704),
            new UnitDefinition(UiText.Get("Unit.FootSecond", "Foot/Second"), "ft/s", 0.3048),
            new UnitDefinition(UiText.Get("Unit.Knot", "Knot"), "kn", 0.514444),
        ],
        UnitCategory.Time =>
        [
            new UnitDefinition(UiText.Get("Unit.Millisecond", "Millisecond"), "ms", 0.001),
            new UnitDefinition(UiText.Get("Unit.Second", "Second"), "s", 1),
            new UnitDefinition(UiText.Get("Unit.Minute", "Minute"), "min", 60),
            new UnitDefinition(UiText.Get("Unit.Hour", "Hour"), "h", 3600),
            new UnitDefinition(UiText.Get("Unit.Day", "Day"), "d", 86400),
            new UnitDefinition(UiText.Get("Unit.Week", "Week"), "wk", 604800),
            new UnitDefinition(UiText.Get("Unit.Month", "Month"), "mo", 2592000),
            new UnitDefinition(UiText.Get("Unit.Year", "Year"), "yr", 31536000),
        ],
        UnitCategory.Pressure =>
        [
            new UnitDefinition(UiText.Get("Unit.Pascal", "Pascal"), "Pa", 1),
            new UnitDefinition(UiText.Get("Unit.Kilopascal", "Kilopascal"), "kPa", 1000),
            new UnitDefinition(UiText.Get("Unit.Bar", "Bar"), "bar", 100000),
            new UnitDefinition(UiText.Get("Unit.PSI", "PSI"), "psi", 6894.76),
            new UnitDefinition(UiText.Get("Unit.Atmosphere", "Atmosphere"), "atm", 101325),
            new UnitDefinition(UiText.Get("Unit.mmHg", "mmHg"), "mmHg", 133.322),
        ],
        UnitCategory.Energy =>
        [
            new UnitDefinition(UiText.Get("Unit.Joule", "Joule"), "J", 1),
            new UnitDefinition(UiText.Get("Unit.Kilojoule", "Kilojoule"), "kJ", 1000),
            new UnitDefinition(UiText.Get("Unit.Calorie", "Calorie"), "cal", 4.184),
            new UnitDefinition(UiText.Get("Unit.Kilocalorie", "Kilocalorie"), "kcal", 4184),
            new UnitDefinition(UiText.Get("Unit.Watthour", "Watt-hour"), "Wh", 3600),
            new UnitDefinition(UiText.Get("Unit.Kilowatthour", "Kilowatt-hour"), "kWh", 3600000),
            new UnitDefinition(UiText.Get("Unit.BTU", "BTU"), "BTU", 1055.06),
        ],
        UnitCategory.Power =>
        [
            new UnitDefinition(UiText.Get("Unit.Watt", "Watt"), "W", 1),
            new UnitDefinition(UiText.Get("Unit.Kilowatt", "Kilowatt"), "kW", 1000),
            new UnitDefinition(UiText.Get("Unit.Megawatt", "Megawatt"), "MW", 1000000),
            new UnitDefinition(UiText.Get("Unit.Horsepower", "Horsepower"), "hp", 745.7),
        ],
        UnitCategory.Angle =>
        [
            new UnitDefinition(UiText.Get("Unit.Degree", "Degree"), "deg", 1),
            new UnitDefinition(UiText.Get("Unit.Radian", "Radian"), "rad", 57.29577951308232),
            new UnitDefinition(UiText.Get("Unit.Gradian", "Gradian"), "grad", 0.9),
        ],
        UnitCategory.DigitalStorage =>
        [
            new UnitDefinition(UiText.Get("Unit.Bit", "Bit"), "bit", 0.125),
            new UnitDefinition(UiText.Get("Unit.Byte", "Byte"), "B", 1),
            new UnitDefinition(UiText.Get("Unit.Kilobyte", "Kilobyte"), "KB", 1024),
            new UnitDefinition(UiText.Get("Unit.Megabyte", "Megabyte"), "MB", 1048576),
            new UnitDefinition(UiText.Get("Unit.Gigabyte", "Gigabyte"), "GB", 1073741824),
            new UnitDefinition(UiText.Get("Unit.Terabyte", "Terabyte"), "TB", 1099511627776),
        ],
        _ => []
    };
}
