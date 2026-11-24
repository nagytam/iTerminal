using iConsole.Commands;
using iConsole.Data;
using iConsole.Localization;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+Z: the focused file panel takes the whole width, for a long name or a narrow terminal; the other panel is
/// hidden, not closed, and comes back on the next press. Ctrl+Z is the rename undo, so this one carries Shift.
/// </summary>
public class TogglePanelZoomCommand(Content? content) : Command
{
    public override string Title => UiText.Title("Zoom the focused panel");
    public override string Description => UiText.Describe("Show the focused file panel across the whole width, or both panels again.");

    public override Key? Key { get; init; } = new Key(ConsoleKey.Z, true, false, true);

    public override bool CanExecute() => content is not null;

    public override Task ExecuteAsync(object? args)
    {
        content?.ToggleZoomedPanel();
        return Task.CompletedTask;
    }
}
