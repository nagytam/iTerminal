using iTerminal.DevelopmentStudio.Services.Analysis;

namespace iTerminal.DevelopmentStudio.Test.Services.Analysis;

[TestClass]
public class CoberturaParserTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TotalsCountTheInstrumentedLinesAndTheOnesThatRan()
    {
        const string xml = """
            <?xml version="1.0"?>
            <coverage line-rate="0.5">
              <sources><source>/src/app</source></sources>
              <packages><package><classes>
                <class filename="Calc.cs"><lines>
                  <line number="3" hits="2"/>
                  <line number="4" hits="0"/>
                </lines></class>
                <class filename="Other.cs"><lines>
                  <line number="1" hits="1"/>
                  <line number="2" hits="0"/>
                </lines></class>
              </classes></package></packages>
            </coverage>
            """;

        var report = CoberturaParser.Parse(xml);

        Assert.AreEqual(2, report.Files.Count);
        Assert.AreEqual(4, report.ValidLines);
        Assert.AreEqual(2, report.CoveredLines);
        Assert.AreEqual(50.0, report.Percent, 0.001);
        var calc = report.Files.Single(f => f.Path.EndsWith("Calc.cs", StringComparison.Ordinal));
        Assert.AreEqual(2, calc.Hits[3]);
        Assert.AreEqual(0, calc.Hits[4]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFileInTwoClassesHasItsHitsSummed()
    {
        const string xml = """
            <coverage><sources><source>/src</source></sources><packages><package><classes>
              <class filename="A.py"><lines><line number="1" hits="1"/></lines></class>
              <class filename="A.py"><lines><line number="1" hits="2"/><line number="2" hits="0"/></lines></class>
            </classes></package></packages></coverage>
            """;

        var file = CoberturaParser.Parse(xml).Files.Single();

        Assert.AreEqual(3, file.Hits[1]);
        Assert.AreEqual(2, file.ValidLines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANonXmlReportIsRefused()
    {
        Assert.ThrowsExactly<System.Xml.XmlException>(() => CoberturaParser.Parse("not a report"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyReportHasFullCoverageAndNoFiles()
    {
        var report = CoberturaParser.Parse("<coverage><packages/></coverage>");

        Assert.AreEqual(0, report.Files.Count);
        Assert.AreEqual(100.0, report.Percent);
    }
}
