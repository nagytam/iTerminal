using iConsole.Services;

namespace iConsole.Test.Controls;

/// <summary>Text and file readers that threw for null (found by fuzzing).</summary>
[TestClass]
public class ControlsReviewBatch33Test
{
    /// <summary>Calls an internal static method of iConsole (the assembly shows its internals to no test project).</summary>
    private static object? Internal(string typeName, string method, params object?[] arguments)
        => typeof(HtmlTextExtractor).Assembly.GetType("iConsole.Services." + typeName)!
            .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, arguments);

    [Timeout(60_000)]
    [TestMethod]
    public void HtmlTextExtractor_Lines_OfNull_AreNone()
    {
        Assert.AreEqual(0, HtmlTextExtractor.Lines((string?)null).Count(l => l.Length > 0));
        Assert.AreEqual(0, HtmlTextExtractor.Lines((byte[]?)null).Count(l => l.Length > 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MimeMessageReader_QuotedPrintableAndCharsetDecode_AcceptNull()
    {
        Assert.AreEqual(0, ((byte[])Internal("MimeMessageReader", "QuotedPrintable", (object?)null)!).Length);
        Assert.AreEqual(string.Empty, Internal("MimeMessageReader", "CharsetDecode", null, null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MobiTextReader_PalmDocDecompress_OfNull_IsEmpty()
    {
        Assert.AreEqual(0, ((byte[])Internal("MobiTextReader", "PalmDocDecompress", (object?)null)!).Length);
    }
}
