using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// A titled, bordered container that hosts other controls — the composable form of the section
/// bands dialogs draw by hand. It clears its interior, draws a themed double-line border with the
/// <see cref="Title"/> embedded in the top edge, then renders its child controls (positioned
/// relative to its interior). It is not itself focusable; Tab moves to whatever it contains.
/// </summary>
public class GroupBox : Control
{
    private string _title = string.Empty;
    private IControl? _content;

    public GroupBox(IThemeService themeService) : base(themeService)
    {
        CanFocus = false;
        ViewSize = new Size(30, 5);
    }

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                StateHasChanged();
            }
        }
    }

    /// <summary>Top-left of the area inside the border, where hosted content is laid out.</summary>
    public Position ContentPosition => new(1, 1);

    /// <summary>Size of the area inside the border.</summary>
    public Size ContentSize => new(
        Math.Max(0, (int)ViewSize.Width - 2),
        Math.Max(0, (int)ViewSize.Height - 2));

    /// <summary>
    /// Hosts a single control sized to fill the interior. Convenience for the common case of a
    /// <see cref="FormPanel"/> (or list) inside a captioned box; multiple children can still be
    /// added with <see cref="Control.AddControl"/> and positioned manually.
    /// </summary>
    public void SetContent(IControl content)
    {
        if (_content is not null)
        {
            RemoveControl(_content);
        }

        _content = content;
        AddControl(content);
        LayoutContent();
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutContent();
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        iConsole.Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, colorScheme);
        DrawBorder(buffer, ZOrder, AbsolutePosition, ViewSize, BuildHeader((int)ViewSize.Width), string.Empty,
            colorScheme, VisualState.Normal);

        LayoutContent();
        base.Draw(buffer);
    }

    private void LayoutContent()
    {
        if (_content is null)
        {
            return;
        }

        _content.Position = ContentPosition;
        _content.ViewSize = ContentSize;
    }

    /// <summary>Builds the inner span of the top border: <c>══ Title ══…</c>, sized to fit between the corners.</summary>
    private string BuildHeader(int width)
    {
        var inner = width - 2;
        if (inner <= 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(Title))
        {
            return Border.Horizontal.Repeat(inner);
        }

        var header = Border.Horizontal.Repeat(Math.Min(2, inner)) + $" {Title} ";
        return header.Length >= inner
            ? header.Cut(inner)
            : header + Border.Horizontal.Repeat(inner - header.Length);
    }
}
