using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Base for small input-form dialogs (Create/Delete/Rename/Copy/Move). Standardizes the field
/// width and the primary/cancel button row so every derived dialog lines up the same way instead
/// of each one hardcoding its own padding.
/// </summary>
public abstract class FormDialog : Dialog
{
    protected const int FieldX = 3;
    protected const int FieldRightMargin = 3;
    protected const int PrimaryButtonWidth = 17;
    protected const int CancelButtonWidth = 15;
    protected const int ButtonRightMargin = 2;

    protected FormDialog(string name, IThemeService themeService)
        : base(name, themeService)
    {
    }

    /// <summary>Width for a field control starting at <see cref="FieldX"/>, clamped to at least 1.</summary>
    protected long FieldWidth => Math.Max(1, ViewSize.Width - FieldX - FieldRightMargin);

    /// <summary>Positions the primary action button (bottom-left) and Cancel button (bottom-right) on the same row.</summary>
    protected void LayoutButtonRow(IControl primaryButton, IControl cancelButton)
    {
        var y = Math.Max(0, ViewSize.Height - 2);
        primaryButton.Position = new Position(FieldX, y);
        primaryButton.ViewSize = new Size(PrimaryButtonWidth, 1);

        var cancelX = Math.Max(FieldX, ViewSize.Width - CancelButtonWidth - ButtonRightMargin);
        cancelButton.Position = new Position(cancelX, y);
        cancelButton.ViewSize = new Size(CancelButtonWidth, 1);
    }
}
