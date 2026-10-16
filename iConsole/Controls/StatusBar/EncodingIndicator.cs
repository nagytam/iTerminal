using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.StatusBar;

public class EncodingIndicator : StatusBarItem
{
    private ITextModel _textModel { get; set; }

    public EncodingIndicator(
        ITextModel textModel,
        IThemeService themeService)
        : base(themeService)
    {
        _textModel = textModel;
        _textModel.PropertyChanged += OnTextModelPropertyChanged;
        Name = nameof(EncodingIndicator);
        ViewSize = new Size(14, 1);
    }

    private void OnTextModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITextModel.Encoding))
        {
            StateHasChanged();
        }
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var line = $"{iConsole.Helper.TextEncodings.DisplayName(_textModel.Encoding)}";
        var s = new ConxelString(line, Color.Green, Color.Blue);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
