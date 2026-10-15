using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// Modal Continue/Cancel prompt used by long-running batch operations (e.g. a folder copy/move/
/// delete pipeline) to ask the user how to proceed after a single item fails, without aborting the
/// whole operation. <see cref="WaitForChoiceAsync"/> resolves once either button is pressed: true
/// means Continue (skip the failing item and keep going), false means Cancel (abort the rest).
/// </summary>
public class ErrorPromptDialog : Dialog
{
    private readonly TaskCompletionSource<bool> _choice = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _message;
    private readonly Button _continueButton;
    private readonly Button _cancelButton;

    private sealed class ChoiceCommand(TaskCompletionSource<bool> choice, bool result) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            choice.TrySetResult(result);
            Control?.Parent?.Close();
            return Task.CompletedTask;
        }
    }

    public ErrorPromptDialog(string title, string message, IThemeService themeService)
        : base(title, themeService)
    {
        _message = message;
        _continueButton = new Button("Continue", new ChoiceCommand(_choice, true), themeService) { HotKeyChar = 'C' };
        _cancelButton = new Button("Cancel", new ChoiceCommand(_choice, false), themeService) { HotKeyChar = 'a' };

        AddControl(_continueButton);
        AddControl(_cancelButton);
    }

    /// <summary>
    /// Resolves once the user picks a button. If <paramref name="cancellationToken"/> fires first
    /// (the surrounding operation was cancelled while the prompt was up), this throws
    /// <see cref="OperationCanceledException"/> instead of hanging forever on an abandoned dialog.
    /// </summary>
    public Task<bool> WaitForChoiceAsync(CancellationToken cancellationToken) => _choice.Task.WaitAsync(cancellationToken);

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent != null)
        {
            CenterInParent(new Size(74, 10));
        }

        var buttonWidth = (ViewSize.Width - 6) / 2;
        _continueButton.Position = new Position(2, ViewSize.Height - 2);
        _continueButton.ViewSize = new Size(buttonWidth, 1);
        _cancelButton.Position = new Position(2 + buttonWidth + 2, ViewSize.Height - 2);
        _cancelButton.ViewSize = new Size(buttonWidth, 1);
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
