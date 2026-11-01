using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// Modal prompt shown when a copy or move finds a file already at its target. Five answers and
/// Cancel, and an "apply to all remaining" tick. <see cref="WaitForChoiceAsync"/> resolves with the
/// answer; closing the dialog without one (Esc) is <c>Cancel</c>.
/// </summary>
public class ConflictPromptDialog : Dialog
{
    private readonly TaskCompletionSource<(FileConflictChoice Choice, bool ApplyToAll)> _choice =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _message;
    private readonly CheckBox _applyToAll;
    private readonly Button[] _buttons;
    private volatile bool _abandoned;

    private sealed class ChoiceCommand(ConflictPromptDialog dialog, FileConflictChoice choice) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            dialog.Answer(choice);
            Control?.Parent?.Close();
            return Task.CompletedTask;
        }
    }

    public ConflictPromptDialog(string title, string message, IThemeService themeService)
        : base(title, themeService)
    {
        _message = message;
        _applyToAll = new CheckBox(themeService)
        {
            Label = iConsole.Localization.UiText.Get("Conflict.ApplyToAll", "Apply to all remaining conflicts"),
        };
        AddControl(_applyToAll);

        var labels = new (FileConflictChoice Choice, string Label, string Fallback, char Key)[]
        {
            (FileConflictChoice.Overwrite, "Conflict.Overwrite", "Overwrite", 'O'),
            (FileConflictChoice.Skip, "Conflict.Skip", "Skip", 'S'),
            (FileConflictChoice.Rename, "Conflict.Rename", "Rename", 'R'),
            (FileConflictChoice.KeepNewer, "Conflict.KeepNewer", "Keep newer", 'N'),
            (FileConflictChoice.KeepLarger, "Conflict.KeepLarger", "Keep larger", 'L'),
            (FileConflictChoice.Cancel, "Common.Cancel", "Cancel", 'C'),
        };
        _buttons = labels
            .Select(l => new Button(iConsole.Localization.UiText.Get(l.Label, l.Fallback), new ChoiceCommand(this, l.Choice), themeService) { HotKeyChar = l.Key })
            .ToArray();
        foreach (var button in _buttons)
        {
            AddControl(button);
        }
    }

    internal void Answer(FileConflictChoice choice) => _choice.TrySetResult((choice, _applyToAll.IsChecked));

    /// <summary>Resolves once the user picks an answer. Throws <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/> fires first.</summary>
    public Task<(FileConflictChoice Choice, bool ApplyToAll)> WaitForChoiceAsync(CancellationToken cancellationToken) =>
        _choice.Task.WaitAsync(cancellationToken);

    /// <summary>The operation that asked was cancelled, so nobody will answer. Called from its thread: only records the fact.</summary>
    public void Abandon()
    {
        _abandoned = true;
        _choice.TrySetCanceled();
    }

    public override void Close()
    {
        // Esc, or any close without an answer, is Cancel. The first answer wins, so closing after a button is harmless.
        _choice.TrySetResult((FileConflictChoice.Cancel, false));
        base.Close();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (_abandoned)
        {
            _abandoned = false;
            Close();
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent != null)
        {
            CenterInParent(new Size(74, 12));
        }

        // Two rows of three buttons, then the tick box on the last row above the border.
        var buttonWidth = (ViewSize.Width - 8) / 3;
        for (var i = 0; i < _buttons.Length; i++)
        {
            var row = i / 3;
            var column = i % 3;
            _buttons[i].Position = new Position(2 + column * (buttonWidth + 2), ViewSize.Height - 5 + row * 2);
            _buttons[i].ViewSize = new Size(buttonWidth, 1);
        }

        _applyToAll.Position = new Position(2, ViewSize.Height - 2);
        _applyToAll.ViewSize = new Size(ViewSize.Width - 4, 1);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var lines = _message.Split(Environment.NewLine);
        var y = 2;
        foreach (var line in lines)
        {
            var s = new ConxelString(line, Color.White, Color.DarkRed);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + 2, (uint)(AbsolutePosition.Y + y)), new Size(ViewSize.Width - 4, 1), s);
            y++;
        }
    }
}
