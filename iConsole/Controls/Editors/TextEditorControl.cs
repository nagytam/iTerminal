using iConsole.Commands;
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
        base.Refresh();
    }
}
