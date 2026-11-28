using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class TestDiscoveryParserTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTheVsTestNamesAfterTheListHeader()
    {
        var names = TestDiscoveryParser.Parse(DevelopmentLanguage.DotNet,
        [
            "Test run for /src/App.Test.dll (.NETCoreApp,Version=v11.0)",
            "The following Tests are available:",
            "    App.Test.SumTests.Adds",
            "    App.Test.SumTests.Subtracts",
            "",
        ]);

        CollectionAssert.AreEqual(new[] { "App.Test.SumTests.Adds", "App.Test.SumTests.Subtracts" }, names.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeepsTheNamesOfEverySolutionProject()
    {
        var names = TestDiscoveryParser.Parse(DevelopmentLanguage.DotNet,
        [
            "The following Tests are available:",
            "    A.One.Test",
            "The following Tests are available:",
            "    B.Two.Test",
        ]);

        CollectionAssert.AreEqual(new[] { "A.One.Test", "B.Two.Test" }, names.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnotherLanguageHasNoNames()
    {
        Assert.AreEqual(0, TestDiscoveryParser.Parse(DevelopmentLanguage.Python, ["tests/test_tool.py::test_adds"]).Count);
    }
}
