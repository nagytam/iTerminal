/// <summary>
/// An XPS or OpenXPS package read into its pages. The package's <c>FixedDocumentSequence</c> lists the documents, each
/// <c>FixedDocument</c> lists its pages, and a page carries its size and its <c>Glyphs</c> runs grouped into lines.
///
/// <para>The package is found through the <c>fixedrepresentation</c> relationship and its parts are matched by local
/// name, so an OpenXPS package (which uses its own namespace) reads the same as an XPS one. A package that cannot be
/// read is returned with <see cref="Error"/> set rather than thrown: the viewer shows that text.</para>
/// </summary>
public sealed class XpsDocument
{
    /// <summary>Builds the document from pages already read; the reader in <c>iConsole.Formats</c> does the reading.</summary>
    public XpsDocument(IReadOnlyList<XpsPage> pages, string? title, string? creator, string? error)
    {
        Pages = pages;
        Title = title;
        Creator = creator;
        Error = error;
    }

    /// <summary>The pages in document order; empty when the package could not be read.</summary>
    public IReadOnlyList<XpsPage> Pages { get; }

    /// <summary>The document title from the package's core properties, when it has one.</summary>
    public string? Title { get; }

    /// <summary>The document creator from the package's core properties, when it has one.</summary>
    public string? Creator { get; }

    /// <summary>The reason the package could not be read, or null when it was read.</summary>
    public string? Error { get; }

    /// <summary>True when the package was read, even when it holds no pages.</summary>
    public bool IsValid => Error is null;
}

/// <summary>One page of an XPS or OpenXPS package: its place in the sequence, its size in device-independent pixels, and its text.</summary>
public sealed class XpsPage
{
    public XpsPage(int number, int documentNumber, int pageInDocument, double width, double height, IReadOnlyList<string> lines)
    {
        Number = number;
        DocumentNumber = documentNumber;
        PageInDocument = pageInDocument;
        Width = width;
        Height = height;
        Lines = lines;
    }

    /// <summary>The page's number in the whole package, from 1.</summary>
    public int Number { get; }

    /// <summary>The document the page belongs to, from 1; a package can hold more than one.</summary>
    public int DocumentNumber { get; }

    /// <summary>The page's number inside its own document, from 1.</summary>
    public int PageInDocument { get; }

    /// <summary>The page width, zero when the page does not declare one.</summary>
    public double Width { get; }

    /// <summary>The page height, zero when the page does not declare one.</summary>
    public double Height { get; }

    /// <summary>The page's text, one entry per line; empty for a page drawn only with pictures or paths.</summary>
    public IReadOnlyList<string> Lines { get; }
}
