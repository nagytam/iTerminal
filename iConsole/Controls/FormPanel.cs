using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// Stacks labelled input fields into a form: it renders the labels itself in a left column (with the
/// separators aligned) and positions each field in the right column at a shared left edge, so a row
/// of <see cref="TextBox"/> / <see cref="NumericUpDown"/> / <see cref="ComboBox"/> / etc. lines up
/// instead of each control formatting its own label. Tab order follows the order fields are added.
///
/// <para>Because the panel owns the labels, construct the field controls *without* their own
/// <c>Label</c> and pass the caption to <see cref="AddField"/>; a field may span several rows by
/// setting its <c>ViewSize.Height</c> before it is added (e.g. a vertical <see cref="RadioGroup{T}"/>).</para>
/// </summary>
public class FormPanel : Control
{
    private sealed record Field(string Label, IControl Control);

    private readonly List<Field> _fields = [];

    public FormPanel(IThemeService themeService) : base(themeService)
    {
        CanFocus = false;
        ViewSize = new Size(30, 10);
    }

    /// <summary>Blank rows inserted between consecutive fields.</summary>
    public int RowGap { get; set; }

    /// <summary>Printed between the label column and the field. Aligned across all rows.</summary>
    public string LabelSeparator { get; set; } = ": ";

    public void AddField(string label, IControl field)
    {
        _fields.Add(new Field(label ?? string.Empty, field));
        AddControl(field);
        LayoutFields();
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutFields();
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        LayoutFields();

        var colorScheme = ThemeService.CurrentColorScheme;
        var labelWidth = LabelColumnWidth;
        var separator = labelWidth > 0 ? LabelSeparator : string.Empty;

        var y = 0;
        foreach (var field in _fields)
        {
            var labelText = field.Label.PadLeft(labelWidth) + separator;
            var position = new Position((int)AbsolutePosition.X, (int)AbsolutePosition.Y + y);
            // The caption of the field holding the cursor is drawn in the focus colour: the panel
            // owns the labels, so the fields cannot highlight their own, and in a column of
            // identical wells the caption is the quickest way to see where typing will land.
            var labelForeground = field.Control.IsFocused
                ? colorScheme.SelectedFocusedForeground
                : colorScheme.Foreground;
            buffer.Write(ZOrder, position, new Size(labelText.Length, 1),
                new ConxelString(labelText, labelForeground, colorScheme.Background));

            y += RowHeight(field) + RowGap;
        }

        base.Draw(buffer);
    }

    private int LabelColumnWidth => _fields.Count == 0 ? 0 : _fields.Max(f => f.Label.Length);

    private static int RowHeight(Field field) => Math.Max(1, (int)field.Control.ViewSize.Height);

    private void LayoutFields()
    {
        var labelWidth = LabelColumnWidth;
        var fieldX = labelWidth + (labelWidth > 0 ? LabelSeparator.Length : 0);
        var fieldWidth = Math.Max(1, (int)ViewSize.Width - fieldX);

        var y = 0;
        foreach (var field in _fields)
        {
            var height = RowHeight(field);
            field.Control.Position = new Position(fieldX, y);
            field.Control.ViewSize = new Size(fieldWidth, height);
            y += height + RowGap;
        }
    }
}
