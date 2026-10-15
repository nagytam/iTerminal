using iTerminal.Core.Model.UnitConverter;

namespace iTerminal.Core.Test.Model.UnitConverter;

[TestClass]
public class UnitConverterEngineTest
{
    [TestMethod]
    public void Constructor_DefaultsToLengthMillimeterToCentimeter()
    {
        var engine = new UnitConverterEngine();

        Assert.AreEqual(UnitCategory.Length, engine.Category);
        Assert.AreEqual(0, engine.FromUnitIndex);
        Assert.AreEqual(1, engine.ToUnitIndex);
        Assert.AreEqual("0.1", engine.ResultText);
    }

    [TestMethod]
    public void SetCategory_ResetsUnitIndicesAndUnitList()
    {
        var engine = new UnitConverterEngine();

        engine.SetCategory(UnitCategory.Mass);

        Assert.AreEqual(UnitCategory.Mass, engine.Category);
        Assert.AreEqual(0, engine.FromUnitIndex);
        Assert.AreEqual(1, engine.ToUnitIndex);
        Assert.AreEqual(UnitCatalog.GetUnits(UnitCategory.Mass).Count, engine.Units.Count);
        Assert.AreEqual("Kilogram", engine.Units[2].Name);
    }

    [TestMethod]
    public void SetCategory_SameCategory_IsNoOp()
    {
        var engine = new UnitConverterEngine();
        engine.SetFromUnit(2);

        engine.SetCategory(UnitCategory.Length);

        Assert.AreEqual(2, engine.FromUnitIndex);
    }

    [TestMethod]
    public void SetFromUnit_OutOfRange_IsIgnored()
    {
        var engine = new UnitConverterEngine();

        engine.SetFromUnit(999);
        engine.SetFromUnit(-1);

        Assert.AreEqual(0, engine.FromUnitIndex);
    }

    [TestMethod]
    public void SetToUnit_OutOfRange_IsIgnored()
    {
        var engine = new UnitConverterEngine();

        engine.SetToUnit(999);

        Assert.AreEqual(1, engine.ToUnitIndex);
    }

    [TestMethod]
    public void SwapUnits_ExchangesFromAndToIndicesAndRecomputes()
    {
        var engine = new UnitConverterEngine();

        engine.SwapUnits();

        Assert.AreEqual(1, engine.FromUnitIndex);
        Assert.AreEqual(0, engine.ToUnitIndex);
        Assert.AreEqual("10", engine.ResultText);
    }

    [TestMethod]
    public void SetInputText_InvalidNumber_ClearsResult()
    {
        var engine = new UnitConverterEngine();

        engine.SetInputText("not a number");

        Assert.AreEqual(string.Empty, engine.ResultText);
    }

    [TestMethod]
    public void SetInputText_EmptyString_ClearsResult()
    {
        var engine = new UnitConverterEngine();

        engine.SetInputText(string.Empty);

        Assert.AreEqual(string.Empty, engine.ResultText);
    }

    [TestMethod]
    public void SetInputText_NegativeNumber_ComputesSignedResult()
    {
        var engine = new UnitConverterEngine();

        engine.SetInputText("-5");

        Assert.AreEqual("-0.5", engine.ResultText);
    }

    [TestMethod]
    public void Length_MileToKilometer_ComputesExpectedValue()
    {
        var engine = new UnitConverterEngine();
        engine.SetFromUnit(7); // Mile
        engine.SetToUnit(3);   // Kilometer

        engine.SetInputText("1");

        Assert.AreEqual(1.609344, double.Parse(engine.ResultText), 1e-6);
    }

    [TestMethod]
    public void Mass_KilogramToPound_ComputesExpectedValue()
    {
        var engine = new UnitConverterEngine();
        engine.SetCategory(UnitCategory.Mass);
        engine.SetFromUnit(2); // Kilogram
        engine.SetToUnit(5);   // Pound

        engine.SetInputText("1");

        Assert.AreEqual(2.20462, double.Parse(engine.ResultText), 1e-4);
    }

    [TestMethod]
    public void DigitalStorage_KilobyteToByte_ComputesExactValue()
    {
        var engine = new UnitConverterEngine();
        engine.SetCategory(UnitCategory.DigitalStorage);
        engine.SetFromUnit(2); // Kilobyte
        engine.SetToUnit(1);   // Byte

        engine.SetInputText("1");

        Assert.AreEqual("1024", engine.ResultText);
    }

    [TestMethod]
    public void Angle_DegreeToRadian_ComputesExpectedValue()
    {
        var engine = new UnitConverterEngine();
        engine.SetCategory(UnitCategory.Angle);
        engine.SetFromUnit(0); // Degree
        engine.SetToUnit(1);   // Radian

        engine.SetInputText("180");

        Assert.AreEqual(Math.PI, double.Parse(engine.ResultText), 1e-6);
    }

    [TestMethod]
    public void Temperature_CelsiusToFahrenheit_ComputesExactValue()
    {
        var engine = new UnitConverterEngine();
        engine.SetCategory(UnitCategory.Temperature);
        engine.SetFromUnit(0); // Celsius
        engine.SetToUnit(1);   // Fahrenheit

        engine.SetInputText("100");

        Assert.AreEqual("212", engine.ResultText);
    }

    [TestMethod]
    public void Temperature_FahrenheitToKelvin_ComputesExpectedValue()
    {
        var engine = new UnitConverterEngine();
        engine.SetCategory(UnitCategory.Temperature);
        engine.SetFromUnit(1); // Fahrenheit
        engine.SetToUnit(2);   // Kelvin

        engine.SetInputText("32");

        Assert.AreEqual(273.15, double.Parse(engine.ResultText), 1e-6);
    }

    [TestMethod]
    public void Temperature_KelvinToCelsius_ComputesExactValue()
    {
        var engine = new UnitConverterEngine();
        engine.SetCategory(UnitCategory.Temperature);
        engine.SetFromUnit(2); // Kelvin
        engine.SetToUnit(0);   // Celsius

        engine.SetInputText("0");

        Assert.AreEqual(-273.15, double.Parse(engine.ResultText), 1e-6);
    }

    [TestMethod]
    public void UnitCatalog_EveryCategory_HasAtLeastTwoUnits()
    {
        foreach (var category in Enum.GetValues<UnitCategory>())
        {
            var units = UnitCatalog.GetUnits(category);
            Assert.IsTrue(units.Count >= 2, $"{category} should expose at least two units");
        }
    }

    [TestMethod]
    public void UnitCatalog_GetCategoryName_ReturnsNonEmptyForEveryCategory()
    {
        foreach (var category in Enum.GetValues<UnitCategory>())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(UnitCatalog.GetCategoryName(category)));
        }
    }
}
