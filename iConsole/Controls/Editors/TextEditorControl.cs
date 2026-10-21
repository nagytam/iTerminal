using iConsole.Commands;
using iConsole.Helper;
using iConsole.Data;
using iConsole.Controls.Viewers;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.Editors;

public class TextEditorControl 
    : TextViewerControl, IDirtyModel, IDirtyReadOnlyModel, IReadOnly
{
    private bool _isDirty = false;
    public bool IsDirty
    {
        get
        {
            return _isDirty;
        }
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    private bool _isReadOnly = false;
    public bool IsReadOnly
    {
        get
        {
            return _isReadOnly;
        }
        set
        {
            if (value != _isReadOnly)
            {
                _isReadOnly = value;
                OnPropertyChanged(nameof(IsReadOnly));
            }
        }
    }

    public TextEditorControl(
        IThemeService themeService)
        : base(true, themeService)
    {
        IsCursorVisible = true;

        AddCommand(new TypeCommand());
        AddCommand(new BackspaceCommand(TextModel, ScrollableModel));
        AddCommand(new DeleteCharacterCommand(TextModel, ScrollableModel));
        AddCommand(new DeleteLineCommand(TextModel, ScrollableModel));
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    /// <summary>
    /// With text selected, Backspace and Delete remove it, and typing or Enter replaces it (the key then goes on to
    /// type or split the line at the cursor the removal left). A read-only editor can select but not change.
    /// </summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (HasSelection && !IsReadOnly && IsEnabled && !keyEvent.Control && !keyEvent.Alt)
        {
            if (!keyEvent.Shift && keyEvent.ConsoleKey is ConsoleKey.Backspace or ConsoleKey.Delete)
            {
                RemoveSelectedText();
                IsDirty = true;
                return true;
            }

            if (ConsoleKeyHelper.IsTypingKey(keyEvent) || keyEvent.ConsoleKey == ConsoleKey.Enter)
            {
                RemoveSelectedText();
                IsDirty = true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Refresh()
    {
        if (TextModel.Lines.Count > 0)
        {
            ScrollableModel.ScrollableSize = new Data.Size(
                TextModel.Lines.Max(l => l.Length),
                TextModel.Lines.Count);
        }
        else
        {
            ScrollableModel.ScrollableSize = new Data.Size(0, 0);
        }
        KeepCursorInText();
        base.Refresh();
    }
}
