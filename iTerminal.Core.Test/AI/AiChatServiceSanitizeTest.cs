using iTerminal.Core.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace iTerminal.Core.Test.AI;

[TestClass]
public class AiChatServiceSanitizeTest
{
    private static string Sanitize(string text)
    {
        var method = typeof(AiChatService).GetMethod("Sanitize",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [text])!;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sanitize_ThinkTags_AreRemoved()
    {
        var input = "<think>internal reasoning here</think>Hello!";
        Assert.AreEqual("Hello!", Sanitize(input));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sanitize_MultilineThinkBlock_IsRemoved()
    {
        var input = "<think>\nline1\nline2\n</think>\nActual response.";
        Assert.AreEqual("Actual response.", Sanitize(input));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sanitize_AnsiColorCode_IsRemoved()
    {
        var input = "\x1b[32mHello\x1b[0m";
        Assert.AreEqual("Hello", Sanitize(input));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sanitize_AnsiCursorSequence_IsRemoved()
    {
        var input = "text\x1b[2Jmore";
        Assert.AreEqual("textmore", Sanitize(input));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sanitize_PlainText_IsUnchanged()
    {
        var input = "This is a plain response.";
        Assert.AreEqual(input, Sanitize(input));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sanitize_ThinkTagAndAnsi_BothRemoved()
    {
        var input = "<think>reasoning</think>\x1b[1mBold answer\x1b[0m";
        Assert.AreEqual("Bold answer", Sanitize(input));
    }
}
