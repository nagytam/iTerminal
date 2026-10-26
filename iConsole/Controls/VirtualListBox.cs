using iConsole.Model;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A list that reads only the rows on screen from an <see cref="IVirtualDataSource{T}"/>, so it stays as quick with ten
/// million rows as with ten, and can show rows that are still being loaded. Keys, wheel and click work as in
/// <see cref="ListBox{T}"/>, but there is no marking and no per-row projection; see <see cref="VirtualRowsControl{T}"/>.
/// </summary>
public class VirtualListBox<T> : VirtualRowsControl<T>
{
    public VirtualListBox(IVirtualDataSource<T> source, IThemeService themeService) : base(source, themeService)
    {
    }

    /// <summary>The text of a row. Defaults to <c>ToString()</c>.</summary>
    public Func<T, string> TextSelector { get; set; } = static item => item?.ToString() ?? string.Empty;

    protected override string RowText(T item, int index) => TextSelector(item);
}
