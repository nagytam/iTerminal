using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.StatusBar;

public class DirtyIndicator : StatusBarItem
{
    private IDirtyReadOnlyModel _dirtyModel { get; set; }

    public DirtyIndicator(
        IDirtyReadOnlyModel dirtyModel,
        IThemeService themeService)
        : base(themeService)
    {
        _dirtyModel = dirtyModel;
        _dirtyModel.PropertyChanged += OnPropertyChanged;
        Name = nameof(DirtyIndicator);
        ViewSize = new Size(10, 1);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var s = new ConxelString($"{(_dirtyModel.IsDirty ? "Changed" : "Unchanged")}", Color.Green, Color.Blue);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
