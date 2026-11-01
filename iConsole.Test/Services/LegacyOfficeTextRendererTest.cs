using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>
/// The Office 97–2003 readers on damaged input. The real files are covered by the fixture tests; this checks that a
/// file that is not a compound document is reported, not thrown.
/// </summary>
[TestClass]
public class LegacyOfficeTextRendererTest
{
    private static readonly byte[] NotCompound = System.Text.Encoding.ASCII.GetBytes("this is plain text, not an OLE2 file");

    [Timeout(60_000)]
    [TestMethod]
    public void Word_NotACompoundFile_SaysTheDocumentCouldNotBeRead()
    {
        List<string> lines = [.. LegacyOfficeTextRenderer.RenderWord(NotCompound)];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the document:"), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Workbook_NotACompoundFile_SaysTheWorkbookCouldNotBeRead()
    {
        List<string> lines = [.. LegacyOfficeTextRenderer.RenderWorkbook(NotCompound)];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the workbook:"), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Presentation_NotACompoundFile_SaysThePresentationCouldNotBeRead()
    {
        List<string> lines = [.. LegacyOfficeTextRenderer.RenderPresentation(NotCompound)];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the presentation:"), string.Join("\n", lines));
    }
}
