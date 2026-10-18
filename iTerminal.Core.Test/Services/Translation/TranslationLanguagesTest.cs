using iTerminal.Core.Services.Translation;

namespace iTerminal.Core.Test.Services.Translation;

[TestClass]
public class TranslationLanguagesTest
{
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

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("xx")]
    [DataRow("auto")]
    public void ACodeThatIsNotOfferedFindsNothing(string? code)
    {
        Assert.IsNull(TranslationLanguages.Find(code));
    }

    [TestMethod]
    public void ANameFallsBackToTheCode()
    {
        Assert.AreEqual("German", TranslationLanguages.NameOf("de"));
        Assert.AreEqual("xx", TranslationLanguages.NameOf("xx"));
    }

    [TestMethod]
    public void TheListHasNoDuplicatesAndIsSortedByName()
    {
        var codes = TranslationLanguages.All.Select(l => l.Code).ToList();

        Assert.AreEqual(codes.Count, codes.Distinct().Count());
        var names = TranslationLanguages.All.Select(l => l.Name).ToList();
        CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
    }
}
