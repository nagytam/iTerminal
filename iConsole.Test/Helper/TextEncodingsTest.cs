using System.Text;
using iConsole.Helper;
using iConsole.Model;

namespace iConsole.Test.Helper;

[TestClass]
public class TextEncodingsTest
{
    [TestMethod]
    public void Decode_ValidUtf8_IsUtf8WithoutBom()
    {
        string text = TextEncodings.Decode(Encoding.UTF8.GetBytes("árvíztűrő"), out Encoding encoding);

        Assert.AreEqual("árvíztűrő", text);
        Assert.AreEqual(65001, encoding.CodePage);
        Assert.AreEqual(0, encoding.GetPreamble().Length);
    }

    [TestMethod]
    public void Decode_BomIsHonouredAndNotPartOfTheText()
    {
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. "héé"u8.ToArray()];

        Assert.AreEqual("héé", TextEncodings.Decode(bytes, out Encoding utf8));
        Assert.AreEqual(65001, utf8.CodePage);

        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("héé")];
        Assert.AreEqual("héé", TextEncodings.Decode(utf16, out Encoding unicode));
        Assert.AreEqual(1200, unicode.CodePage);
    }

    [TestMethod]
    public void Decode_InvalidUtf8_IsNeverLossy()
    {
        byte[] bytes = [0x61, 0xE9, 0x62, 0xFF];

        string text = TextEncodings.Decode(bytes, out Encoding encoding);

        Assert.IsFalse(text.Contains('�'));
        CollectionAssert.AreEqual(bytes, encoding.GetBytes(text));
    }

    [TestMethod]
    public void Reinterpret_RereadsTheBytesUnderAnotherCodePage()
    {
        // "ő" written in Windows-1250 is 0xF5, which Latin-1 reads as "õ".
        byte[] file = Encoding.GetEncoding(1250).GetBytes("tűrő\nsor");
        TextModel model = new();
        string decoded = TextEncodings.Decode(file, out Encoding detected);
        foreach (string line in decoded.Split('\n'))
        {
            model.Lines.Add(line);
        }
        model.Encoding = Encoding.Latin1;

        TextEncodings.Reinterpret(model, Encoding.GetEncoding(1250));

        CollectionAssert.AreEqual(new[] { "tűrő", "sor" }, model.Lines.ToArray());
        Assert.AreEqual(1250, model.Encoding.CodePage);
    }

    [TestMethod]
    public void Reinterpret_KeepsUnsavedEdits()
    {
        TextModel model = new() { Encoding = Encoding.Latin1 };
        model.Lines.Add("edited line");

        TextEncodings.Reinterpret(model, Encoding.GetEncoding(1250));

        CollectionAssert.AreEqual(new[] { "edited line" }, model.Lines.ToArray());
    }

    [TestMethod]
    public void Next_CyclesBothWaysAndWraps()
    {
        Encoding first = TextEncodings.Available[0];
        Encoding last = TextEncodings.Available[^1];

        Assert.AreEqual(TextEncodings.Available[1].CodePage, TextEncodings.Next(first).CodePage);
        Assert.AreEqual(first.CodePage, TextEncodings.Next(last).CodePage);
        Assert.AreEqual(last.CodePage, TextEncodings.Next(first, backwards: true).CodePage);
    }

    [TestMethod]
    public void Next_FromAnUnlistedEncoding_StartsAtTheEdge()
    {
        Encoding unlisted = Encoding.UTF32;

        Assert.AreEqual(TextEncodings.Available[0].CodePage, TextEncodings.Next(unlisted).CodePage);
    }

    [TestMethod]
    public void Available_HoldsTheLegacyPagesTheSwitchOffers()
    {
        foreach (int page in new[] { 1250, 1251, 1252, 852 })
        {
            Assert.IsTrue(TextEncodings.Available.Any(e => e.CodePage == page), $"code page {page}");
        }
    }
}
