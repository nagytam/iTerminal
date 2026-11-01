using iTerminal.Core.Services.Translation;

namespace iTerminal.Core.Test.Services.Translation;

[TestClass]
public class LanguageDetectorTest
{
    private readonly LanguageDetector _detector = new();

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("The quick brown fox jumps over the lazy dog and it is not that far from here.", "en")]
    [DataRow("Der Hund ist nicht in dem Haus, aber ich habe ihn mit der Katze gesehen.", "de")]
    [DataRow("Je ne sais pas ce que nous allons faire dans la maison avec les enfants.", "fr")]
    [DataRow("No sé lo que vamos a hacer con los niños en la casa, pero es muy bonito.", "es")]
    [DataRow("Non so che cosa faremo con i bambini, ma è una casa molto bella e grande.", "it")]
    [DataRow("Eu não sei o que vamos fazer com as crianças na casa, mas é muito bonita.", "pt")]
    [DataRow("Ik weet niet wat we met de kinderen in het huis gaan doen, maar het is mooi.", "nl")]
    [DataRow("Jag vet inte vad vi ska göra med barnen i huset, men det är mycket fint.", "sv")]
    [DataRow("Nie wiem, co zrobimy z dziećmi w domu, ale to jest bardzo ładne miejsce.", "pl")]
    [DataRow("Ez egy nagyon szép ház, de nem tudom, hogy mit csinálunk a gyerekekkel.", "hu")]
    [DataRow("Bu çok güzel bir ev ama çocuklarla ne yapacağımızı bilmiyorum, ben de değil.", "tr")]
    public void ALatinTextIsToldByItsCommonWords(string text, string expected)
    {
        Assert.AreEqual(expected, _detector.Detect(text)?.Code);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("これは日本語の文章です。ひらがなとカタカナがあります。", "ja")]
    [DataRow("这是一个很长的中文句子,没有假名。", "zh")]
    [DataRow("이것은 한국어 문장입니다", "ko")]
    [DataRow("Это длинное предложение на русском языке, которое не содержит ничего другого.", "ru")]
    [DataRow("Це довге речення українською мовою, яке містить літери і та ї.", "uk")]
    [DataRow("Αυτή είναι μια πρόταση στα ελληνικά", "el")]
    [DataRow("זהו משפט בעברית", "he")]
    [DataRow("هذه جملة باللغة العربية", "ar")]
    [DataRow("این یک جمله به زبان فارسی است که چای دارد", "fa")]
    [DataRow("นี่คือประโยคภาษาไทย", "th")]
    [DataRow("यह हिंदी में एक वाक्य है", "hi")]
    public void AScriptThatBelongsToOneLanguageDecidesIt(string text, string expected)
    {
        LanguageDetection? detection = _detector.Detect(text);

        Assert.AreEqual(expected, detection?.Code);
        Assert.IsTrue(detection!.Confidence > 0.5, "a text wholly in the script is a sure guess");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KanaWinsOverHanBecauseJapaneseIsMostlyKanji()
    {
        Assert.AreEqual("ja", _detector.Detect("私は東京の大学で勉強しています")?.Code);
        Assert.AreEqual("ja", _detector.Detect("東京の大学でべんきょうします")?.Code);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("12345 67890 !!!")]
    public void NoLettersMeansNoGuess(string text)
    {
        Assert.IsNull(_detector.Detect(text));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALatinTextWithNoTellingWordIsNotGuessed()
    {
        Assert.IsNull(_detector.Detect("Xylophone quartz"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADistinctiveLetterCountsAsEvidence()
    {
        Assert.AreEqual("de", _detector.Detect("Straße")?.Code);
        Assert.AreEqual("es", _detector.Detect("¿Cómo?")?.Code);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoreEvidenceMakesAMoreConfidentGuess()
    {
        double little = _detector.Detect("the cat")!.Confidence;
        double lots = _detector.Detect("the cat is on the mat and it is not with the dog in the house")!.Confidence;

        Assert.IsTrue(lots > little, $"{lots} should be above {little}");
        Assert.IsTrue(lots <= 0.99);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryCodeTheDetectorCanReturnIsOneTheTranslatorOffers()
    {
        string[] samples =
        [
            "the and of to in is that it for with was are this", "der die das und ist nicht ich mit", "le la les des et est un une",
            "я не знаю что это", "це не те що і ї", "ђ ћ љ њ", "한국어", "日本語かな", "中文", "ไทย", "עברית", "ελληνικά", "عربي", "پ چ ژ گ فارسی",
        ];

        foreach (string sample in samples)
        {
            LanguageDetection? detection = _detector.Detect(sample);
            if (detection is not null)
            {
                Assert.IsNotNull(TranslationLanguages.Find(detection.Code), $"{detection.Code} for \"{sample}\" is not an offered language");
            }
        }
    }
}
