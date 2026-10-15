namespace iTerminal.Core.Data.Documents;

/// <summary>
/// The extracted text of one document, keyed by the same path as its <see cref="DocumentEntry"/>.
///
/// <para>It is a table of its own rather than a column on the metadata row for one reason: the
/// metadata is loaded into memory in full and the text never is. A folder of a few thousand PDFs is
/// a megabyte of metadata and can be hundreds of megabytes of text, so the two have to be readable
/// apart. Search goes through the FTS index, and this table is what the preview pane reads — one
/// row at a time, on a background task, for the one document the cursor is on.</para>
/// </summary>
public class DocumentContentEntry
{
    /// <summary>Full path of the file. Primary key, and the join back to <see cref="DocumentEntry"/>.</summary>
    public string Path { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}
