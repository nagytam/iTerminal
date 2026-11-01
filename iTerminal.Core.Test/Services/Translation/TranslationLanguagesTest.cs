using iTerminal.Core.Services.Translation;

namespace iTerminal.Core.Test.Services.Translation;

[TestClass]
public class TranslationLanguagesTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("de", "de")]
    [DataRow("DE", "de")]
    [DataRow(" pt-BR ", "pt")]
    [DataRow("zh_CN", "zh")]
    [DataRow("no", "nb")]
    [DataRow("nn", "nb")]
    public void RegionalAndAlternativeCodesFindTheirBaseLanguage(string code, string expected)
    {
        Assert.AreEqual(expected, TranslationLanguages.Find(code)?.Code);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("xx")]
    [DataRow("auto")]
    public void ACodeThatIsNotOfferedFindsNothing(string? code)
    {
        Assert.IsNull(TranslationLanguages.Find(code));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANameFallsBackToTheCode()
    {
        Assert.AreEqual("German", TranslationLanguages.NameOf("de"));
        Assert.AreEqual("xx", TranslationLanguages.NameOf("xx"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheListHasNoDuplicatesAndIsSortedByName()
    {
        var codes = TranslationLanguages.All.Select(l => l.Code).ToList();

        Assert.AreEqual(codes.Count, codes.Distinct().Count());
        var names = TranslationLanguages.All.Select(l => l.Name).ToList();
        CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
    }
}
