using iConsole.Controls;
using iConsole.Model;

namespace iConsole.Extensions;

public static class TextBoxExtensions
{
    public static TextBox WithAutoCompletion(this TextBox textBox, IEnumerable<string> completions)
    {
        var completionModel = new CompletionModel(completions);
        var autoCompletion = new AutoCompletion(textBox.TextModel, completionModel, textBox.ThemeService)
        {
            Position = new Data.Position(0, 1),
            ViewSize = new Data.Size(textBox.ViewSize.Width, 5),
            IsVisible = (completions.Any())
        };
        textBox.AddControl(autoCompletion);
        textBox.AutoCompletion = autoCompletion;
        autoCompletion.Owner = textBox;
        autoCompletion.CompletionModel.UpdateMatchingValues(textBox.TextModel.Lines.FirstOrDefault() ?? string.Empty);
        return textBox;
    }

    public static TextBox WithAutoCompletion(this TextBox textBox, ICompletionModel completionModel)
    {
        var autoCompletion = new AutoCompletion(textBox.TextModel, completionModel, textBox.ThemeService)
        {
            Position = new Data.Position(0, 1),
            ViewSize = new Data.Size(textBox.ViewSize.Width, 5),
            IsVisible = (completionModel.Completions.Any())
        };
        textBox.AddControl(autoCompletion);
        textBox.AutoCompletion = autoCompletion;
        autoCompletion.Owner = textBox;
        autoCompletion.CompletionModel.UpdateMatchingValues(textBox.TextModel.Lines.FirstOrDefault() ?? string.Empty);
        return textBox;
    }
}

