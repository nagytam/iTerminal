using iConsole.Data;
using iConsole.Model;
using iConsole.Controls;

namespace iConsole.Commands;

public class AutoCompleteCommand(ITextModel textModel, ICompletionModel completionModel)
    : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Tab, false, false, false);

    public override bool CanExecute()
    {
        return base.CanExecute();
    }

    /// <summary>
    /// How long Tab waits for the match update the last keystroke started. Matching is an in-memory
    /// filter that takes a millisecond or two; the bound only keeps a stuck update from holding the key.
    /// </summary>
    internal static readonly TimeSpan PendingUpdateWait = TimeSpan.FromMilliseconds(150);

    public override async Task ExecuteAsync(object? arg)
    {
        // Matches are computed on a background task per keystroke. A Tab pressed straight after
        // typing used to read the matches of the keystroke *before* — completing to nothing, or to a
        // path that no longer matched what was typed. The update for the current text is the model's
        // UpdateTask; let it land first.
        if (completionModel.UpdateTask is { IsCompleted: false } pending)
        {
            await Task.WhenAny(pending, Task.Delay(PendingUpdateWait));
        }

        if (completionModel.MatchingValues.Any())
        {
            if (Control is AutoCompletion autoCompletion)
            {
                autoCompletion.SuppressNextVisibilityUpdate();
                // Use the highlighted item in the list when available.
                var selectedValue = autoCompletion.GetSelectedValue();
                textModel.Lines[0] = selectedValue ?? completionModel.MatchingValues.First();
            }
            else
            {
                textModel.Lines[0] = completionModel.MatchingValues.First();
            }
            textModel.TriggerPropertyChanged();
            Control?.Hide();
        }
    }
}
