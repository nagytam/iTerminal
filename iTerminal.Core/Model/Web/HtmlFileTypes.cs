namespace iTerminal.Core.Model.Web;

/// <summary>
/// The file extensions that open in the internal Web Browser, both with F3 and with Enter: a page is meant to be
/// browsed, not read as markup. Shared so F3 and Enter never disagree about what a page is.
/// </summary>
public static class HtmlFileTypes
{
    public static bool IsHtmlFile(string? path) =>
        !string.IsNullOrEmpty(path)
        && Path.GetExtension(path).ToLowerInvariant() is ".html" or ".htm" or ".xhtml";
}
