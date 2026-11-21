using iConsole.Xps;

namespace iConsole.Services;

/// <summary>
/// Reads an XPS or OpenXPS document (.xps, .oxps) into lines, page by page in document order. The package is read by
/// <see cref="XpsDocument"/>; this turns its pages into the flat text the viewer and the document index use.
/// </summary>
public static class XpsTextReader
{
    public static IReadOnlyList<string> Read(byte[] content)
    {
        XpsDocument document = XpsDocument.Read(content);
        if (document.Error is not null)
        {
            return [document.Error];
        }

        List<string> lines = [];
        foreach (XpsPage page in document.Pages)
        {
            OfficeTextRenderer.AppendHeading(lines, $"Page {page.Number}");
            lines.AddRange(page.Lines);
            lines.Add(string.Empty);
        }

        return OfficeTextRenderer.Trim(lines);
    }
}
