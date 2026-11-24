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
        if (!base.CanExecute())
        {
            return false;
        }

        // A box whose model lists folders sits in dialogs where Tab is also how focus moves on (Copy, Compress...): it
        // completes only while the list is up, or while the update for the last keystroke is still on its way. With
        // the list down — nothing typed yet, or Esc pressed — Tab is left for the dialog.
        if (completionModel is IBlockingCompletionModel)
        {
            return Control is AutoCompletion { IsPopupOpen: true } || completionModel.UpdateTask is { IsCompleted: false };
        }

        return true;
    }

    /// <summary>
    /// How long Tab waits for the match update the last keystroke started. Matching is an in-memory
    /// filter that takes a millisecond or two; the bound only keeps a stuck update from holding the key.
    /// </summary>
    internal static readonly TimeSpan PendingUpdateWait = TimeSpan.FromMilliseconds(150);

    /// <summary>The same wait for an <see cref="IBlockingCompletionModel"/>, whose update lists a folder (bounded at 750 ms by the model).</summary>
    internal static readonly TimeSpan BlockingUpdateWait = TimeSpan.FromMilliseconds(1000);

    public override async Task ExecuteAsync(object? args)
    {
        // Matches are computed on a background task per keystroke. A Tab pressed straight after
        // typing used to read the matches of the keystroke *before* — completing to nothing, or to a
        // path that no longer matched what was typed. The update for the current text is the model's
        // UpdateTask; let it land first.
        if (completionModel.UpdateTask is { IsCompleted: false } pending)
        {
            // A model that does I/O may take longer than an in-memory filter; Tab waits (asynchronously) for it, or the
            // matches it completes from would still belong to the folder before.
            await Task.WhenAny(pending, Task.Delay(completionModel is IBlockingCompletionModel ? BlockingUpdateWait : PendingUpdateWait));
        }

        if (completionModel.MatchingValues.Any())
        {
            if (Control is AutoCompletion autoCompletion)
            {
                autoCompletion.SuppressNextVisibilityUpdate();
                // Use the highlighted item in the list when available.
                var selectedValue = autoCompletion.GetSelectedValue();
                textModel.Lines[0] = selectedValue ?? completionModel.MatchingValues[0];
            }
            else
            {
                textModel.Lines[0] = completionModel.MatchingValues[0];
            }
            textModel.TriggerPropertyChanged();
            Control?.Hide();
        }
    }
}
