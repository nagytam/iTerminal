using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>What the person asked for when an edited file was about to be closed.</summary>
public enum UnsavedChoice
{
    /// <summary>Write the changes, then close.</summary>
    Save,

    /// <summary>Close and lose the changes.</summary>
    Discard,

    /// <summary>Stay in the editor. Esc is this too, so no key leaves the question unanswered.</summary>
    Cancel,
}

/// <summary>
/// "<c>name</c> has unsaved changes": Save, Discard or Cancel (hot keys S, D and C; Esc cancels). The editor asks it
/// when Esc or Close reaches a file that was changed, so that closing never throws an edit away by accident.
/// <see cref="Choice"/> resolves once, whichever way the dialog ends.
/// </summary>
public class UnsavedChangesDialog : Dialog
{
    private readonly TaskCompletionSource<UnsavedChoice> _choice = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _message;
    private readonly Button _saveButton;
    private readonly Button _discardButton;
    private readonly Button _cancelButton;

    private sealed class ChoiceCommand(TaskCompletionSource<UnsavedChoice> choice, UnsavedChoice result) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            choice.TrySetResult(result);
            Control?.Parent?.Close();
            return Task.CompletedTask;
        }
    }

    public UnsavedChangesDialog(string fileName, IThemeService themeService)
        : base(iConsole.Localization.UiText.Get("Editor.UnsavedChanges", "Unsaved changes"), themeService)
    {
        _message = $"{fileName} has unsaved changes.";
        _saveButton = new Button(iConsole.Localization.UiText.Get("Common.Save", "Save") + " (S)", new ChoiceCommand(_choice, UnsavedChoice.Save), themeService);
        _discardButton = new Button(iConsole.Localization.UiText.Get("Editor.Discard", "Discard") + " (D)", new ChoiceCommand(_choice, UnsavedChoice.Discard), themeService);
        _cancelButton = new Button(iConsole.Localization.UiText.Get("Common.Cancel", "Cancel") + " (C)", new ChoiceCommand(_choice, UnsavedChoice.Cancel), themeService);
        AddControl(_saveButton);
        AddControl(_discardButton);
        AddControl(_cancelButton);
    }

    /// <summary>
    /// S, D and C answer on their own, no Ctrl needed — a dialog that is a single question takes the letter. (A button's own hot
    /// key is Ctrl+letter, and Ctrl+C or Ctrl+D would mean something else to a terminal.) Enter presses the focused button.
    /// </summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("S", "Save the changes");
        yield return ("D", "Discard the changes");
        yield return ("C", "Cancel and keep editing");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            UnsavedChoice? chosen = char.ToLowerInvariant(keyEvent.KeyChar) switch
            {
                's' => UnsavedChoice.Save,
                'd' => UnsavedChoice.Discard,
                'c' => UnsavedChoice.Cancel,
                _ => null,
            };
            if (chosen is { } choice)
            {
                _choice.TrySetResult(choice);
                Close();
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>Resolves when a button is pressed, or when the dialog is closed any other way (Esc), as <see cref="UnsavedChoice.Cancel"/>.</summary>
    public Task<UnsavedChoice> Choice => _choice.Task;

    public override void Close()
    {
        _choice.TrySetResult(UnsavedChoice.Cancel);
        base.Close();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent != null)
        {
            CenterInParent(new Size(Math.Clamp(_message.Length + 8, 46, 80), 7));
        }

        long buttonWidth = Math.Max(1, (ViewSize.Width - 8) / 3);
        long y = Math.Max(0, ViewSize.Height - 2);
        _saveButton.Position = new Position(2, y);
        _saveButton.ViewSize = new Size(buttonWidth, 1);
        _discardButton.Position = new Position(2 + buttonWidth + 2, y);
        _discardButton.ViewSize = new Size(buttonWidth, 1);
        _cancelButton.Position = new Position(2 + (buttonWidth + 2) * 2, y);
        _cancelButton.ViewSize = new Size(buttonWidth, 1);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        ColorScheme colors = ThemeService.CurrentColorScheme;
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 2, (uint)(AbsolutePosition.Y + 2)),
            new Size(Math.Max(0, ViewSize.Width - 4), 1),
            new ConxelString(_message.PadRight((int)Math.Max(0, ViewSize.Width - 4)), colors.Foreground, colors.Background));
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 2, (uint)(AbsolutePosition.Y + 3)),
            new Size(Math.Max(0, ViewSize.Width - 4), 1),
            new ConxelString("Save them before closing?".PadRight((int)Math.Max(0, ViewSize.Width - 4)), colors.DisabledForeground, colors.Background));
    }
}
