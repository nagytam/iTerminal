using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class ConsoleKeyHelperTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleKeyHelperTypeableCharactersContainsLowercaseLetters()
    {
        var chars = ConsoleKeyHelper.TypeableCharacters;
        Assert.IsTrue(chars.Contains('a'));
        Assert.IsTrue(chars.Contains('z'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleKeyHelperTypeableCharactersContainsUppercaseLetters()
    {
        var chars = ConsoleKeyHelper.TypeableCharacters;
        Assert.IsTrue(chars.Contains('A'));
        Assert.IsTrue(chars.Contains('Z'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleKeyHelperTypeableCharactersContainsNumbers()
    {
        var chars = ConsoleKeyHelper.TypeableCharacters;
        Assert.IsTrue(chars.Contains('0'));
        Assert.IsTrue(chars.Contains('9'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleKeyHelperTypeableCharactersContainsSpecialChars()
    {
        var chars = ConsoleKeyHelper.TypeableCharacters;
        Assert.IsTrue(chars.Contains('-'));
        Assert.IsTrue(chars.Contains('_'));
        Assert.IsTrue(chars.Contains(' '));
        Assert.IsTrue(chars.Contains('.'));
        Assert.IsTrue(chars.Contains('/'));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow('(')]
    [DataRow('\'')]
    [DataRow('&')]
    [DataRow('é')]
    [DataRow('ő')]
    [DataRow('€')]
    public void PrintableCharactersAreTypeable(char c) => Assert.IsTrue(ConsoleKeyHelper.IsTypeable(c));

    [Timeout(60_000)]
    [TestMethod]
    [DataRow('\0')]
    [DataRow('\r')]
    [DataRow('\t')]
    [DataRow('\u001b')]
    [DataRow('\u0003')]
    public void ControlCharactersAreNotTypeable(char c) => Assert.IsFalse(ConsoleKeyHelper.IsTypeable(c));

    [Timeout(60_000)]
    [TestMethod]
    public void CtrlAltTogetherIsAltGrAndTypes() =>
        Assert.IsTrue(ConsoleKeyHelper.IsTypingKey(new iConsole.Data.KeyEvent('{', ConsoleKey.B, false, true, true)));

    [Timeout(60_000)]
    [TestMethod]
    public void AltAloneOrCtrlAloneIsAChord()
    {
        Assert.IsFalse(ConsoleKeyHelper.IsTypingKey(new iConsole.Data.KeyEvent('a', ConsoleKey.A, false, true, false)));
        Assert.IsFalse(ConsoleKeyHelper.IsTypingKey(new iConsole.Data.KeyEvent('a', ConsoleKey.A, false, false, true)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleKeyToCharD1()
    {
        var result = ConsoleKey.D1.ConsoleKeyToChar();
        Assert.AreEqual('1', result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleKeyToCharD5()
    {
        var result = ConsoleKey.D5.ConsoleKeyToChar();
        Assert.AreEqual('5', result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleKeyToCharD9()
    {
        var result = ConsoleKey.D9.ConsoleKeyToChar();
        Assert.AreEqual('9', result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleKeyToCharInvalidKey()
    {
        var result = ConsoleKey.A.ConsoleKeyToChar();
        Assert.AreEqual('\0', result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConsoleKeyToCharEscape()
    {
        var result = ConsoleKey.Escape.ConsoleKeyToChar();
        Assert.AreEqual('\0', result);
    }
}
