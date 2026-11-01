namespace iConsole.Model;

/// <summary>
/// A scrollable model whose entries are laid out in several columns, filled row-major (left to right,
/// then the next row). A line is <see cref="ColumnCount"/> consecutive entries.
/// <see cref="IScrollableModel.CursorPosition"/> and <see cref="IScrollableModel.ScrollableSize"/> stay in
/// entries; <see cref="IScrollableModel.ScrollPosition"/> is kept on a line start.
/// </summary>
public interface IColumnScrollableModel : IScrollableModel
{
    /// <summary>Number of columns, at least 1. A single-column model behaves like <see cref="IScrollableModel"/>.</summary>
    int ColumnCount { get; set; }

    /// <summary>Rounds the scroll position down to a line start (a multiple of <see cref="ColumnCount"/>).</summary>
    void AlignScroll();
}
