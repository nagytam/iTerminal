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
        UnitCategory.Length => "Length",
        UnitCategory.Area => "Area",
        UnitCategory.Volume => "Volume",
        UnitCategory.Mass => "Mass",
        UnitCategory.Temperature => "Temperature",
        UnitCategory.Speed => "Speed",
        UnitCategory.Time => "Time",
        UnitCategory.Pressure => "Pressure",
        UnitCategory.Energy => "Energy",
        UnitCategory.Power => "Power",
        UnitCategory.Angle => "Angle",
        UnitCategory.DigitalStorage => "Digital Storage",
        _ => category.ToString()
    };

    public static IReadOnlyList<UnitDefinition> GetUnits(UnitCategory category) => category switch
    {
        UnitCategory.Length =>
        [
            new UnitDefinition("Millimeter", "mm", 0.001),
            new UnitDefinition("Centimeter", "cm", 0.01),
            new UnitDefinition("Meter", "m", 1),
            new UnitDefinition("Kilometer", "km", 1000),
            new UnitDefinition("Inch", "in", 0.0254),
            new UnitDefinition("Foot", "ft", 0.3048),
            new UnitDefinition("Yard", "yd", 0.9144),
            new UnitDefinition("Mile", "mi", 1609.344),
            new UnitDefinition("Nautical Mile", "nmi", 1852),
        ],
        UnitCategory.Area =>
        [
            new UnitDefinition("Square Millimeter", "mm2", 0.000001),
            new UnitDefinition("Square Centimeter", "cm2", 0.0001),
            new UnitDefinition("Square Meter", "m2", 1),
            new UnitDefinition("Hectare", "ha", 10000),
            new UnitDefinition("Square Kilometer", "km2", 1000000),
            new UnitDefinition("Square Inch", "in2", 0.00064516),
            new UnitDefinition("Square Foot", "ft2", 0.09290304),
            new UnitDefinition("Square Yard", "yd2", 0.83612736),
            new UnitDefinition("Acre", "ac", 4046.8564224),
            new UnitDefinition("Square Mile", "mi2", 2589988.110336),
        ],
        UnitCategory.Volume =>
        [
            new UnitDefinition("Milliliter", "mL", 0.001),
            new UnitDefinition("Liter", "L", 1),
            new UnitDefinition("Cubic Meter", "m3", 1000),
            new UnitDefinition("Teaspoon", "tsp", 0.00492892),
            new UnitDefinition("Tablespoon", "tbsp", 0.0147868),
            new UnitDefinition("Fluid Ounce", "fl oz", 0.0295735),
            new UnitDefinition("Cup", "cup", 0.236588),
            new UnitDefinition("Pint", "pt", 0.473176),
            new UnitDefinition("Quart", "qt", 0.946353),
            new UnitDefinition("Gallon", "gal", 3.78541),
            new UnitDefinition("Cubic Inch", "in3", 0.0163871),
            new UnitDefinition("Cubic Foot", "ft3", 28.3168),
        ],
        UnitCategory.Mass =>
        [
            new UnitDefinition("Milligram", "mg", 0.000001),
            new UnitDefinition("Gram", "g", 0.001),
            new UnitDefinition("Kilogram", "kg", 1),
            new UnitDefinition("Metric Ton", "t", 1000),
            new UnitDefinition("Ounce", "oz", 0.0283495),
            new UnitDefinition("Pound", "lb", 0.453592),
            new UnitDefinition("Stone", "st", 6.35029),
            new UnitDefinition("Short Ton", "ton", 907.185),
            new UnitDefinition("Long Ton", "lton", 1016.05),
        ],
        UnitCategory.Temperature =>
        [
            new UnitDefinition("Celsius", "C", 1),
            new UnitDefinition("Fahrenheit", "F", 1),
            new UnitDefinition("Kelvin", "K", 1),
        ],
        UnitCategory.Speed =>
        [
            new UnitDefinition("Meter/Second", "m/s", 1),
            new UnitDefinition("Kilometer/Hour", "km/h", 0.277778),
            new UnitDefinition("Mile/Hour", "mph", 0.44704),
            new UnitDefinition("Foot/Second", "ft/s", 0.3048),
            new UnitDefinition("Knot", "kn", 0.514444),
        ],
        UnitCategory.Time =>
        [
            new UnitDefinition("Millisecond", "ms", 0.001),
            new UnitDefinition("Second", "s", 1),
            new UnitDefinition("Minute", "min", 60),
            new UnitDefinition("Hour", "h", 3600),
            new UnitDefinition("Day", "d", 86400),
            new UnitDefinition("Week", "wk", 604800),
            new UnitDefinition("Month", "mo", 2592000),
            new UnitDefinition("Year", "yr", 31536000),
        ],
        UnitCategory.Pressure =>
        [
            new UnitDefinition("Pascal", "Pa", 1),
            new UnitDefinition("Kilopascal", "kPa", 1000),
            new UnitDefinition("Bar", "bar", 100000),
            new UnitDefinition("PSI", "psi", 6894.76),
            new UnitDefinition("Atmosphere", "atm", 101325),
            new UnitDefinition("mmHg", "mmHg", 133.322),
        ],
        UnitCategory.Energy =>
        [
            new UnitDefinition("Joule", "J", 1),
            new UnitDefinition("Kilojoule", "kJ", 1000),
            new UnitDefinition("Calorie", "cal", 4.184),
            new UnitDefinition("Kilocalorie", "kcal", 4184),
            new UnitDefinition("Watt-hour", "Wh", 3600),
            new UnitDefinition("Kilowatt-hour", "kWh", 3600000),
            new UnitDefinition("BTU", "BTU", 1055.06),
        ],
        UnitCategory.Power =>
        [
            new UnitDefinition("Watt", "W", 1),
            new UnitDefinition("Kilowatt", "kW", 1000),
            new UnitDefinition("Megawatt", "MW", 1000000),
            new UnitDefinition("Horsepower", "hp", 745.7),
        ],
        UnitCategory.Angle =>
        [
            new UnitDefinition("Degree", "deg", 1),
            new UnitDefinition("Radian", "rad", 57.29577951308232),
            new UnitDefinition("Gradian", "grad", 0.9),
        ],
        UnitCategory.DigitalStorage =>
        [
            new UnitDefinition("Bit", "bit", 0.125),
            new UnitDefinition("Byte", "B", 1),
            new UnitDefinition("Kilobyte", "KB", 1024),
            new UnitDefinition("Megabyte", "MB", 1048576),
            new UnitDefinition("Gigabyte", "GB", 1073741824),
            new UnitDefinition("Terabyte", "TB", 1099511627776),
        ],
        _ => []
    };
}
