using iConsole.Controls.Editors;
using iConsole.Services;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The studio's source view: a full text editor (the cursor and selection keys, undo and redo, the clipboard, find and the line
/// tools, folding and bracket matching) that also shows the studio's marks in its gutter. The studio sets the marks on the UI
/// thread; the editor draws them in the gutter's mark column, which is the fold marker's column, so folding is on.
/// </summary>
internal sealed class StudioSourceEditor : TextEditorControl
{
    private IReadOnlyDictionary<int, string> _marks = new Dictionary<int, string>();

    public StudioSourceEditor(IThemeService themeService, ISyntaxHighlightingService highlighting)
        : base(themeService)
    {
        Highlighting = highlighting;
        CanFocus = true;
        ShowLineNumbers = true;
        EnableFolding = true;
    }

    /// <summary>The marks by line index (counted from 0): the stop line, breakpoints, diagnostics and coverage. Replaced as a whole.</summary>
    public void SetMarks(IReadOnlyDictionary<int, string> marks)
    {
        _marks = marks;
        StateHasChanged();
    }

    /// <summary>The mark of the line, or null for none.</summary>
    public string? MarkAt(int line) => _marks.TryGetValue(line, out string? mark) ? mark : null;

    protected override string? GutterMark(int line) => MarkAt(line);
}
