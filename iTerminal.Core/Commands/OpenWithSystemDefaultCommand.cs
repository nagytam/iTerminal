using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Core.Commands;

/// <summary>
/// Enter on a local file that is not a program, and has no program chosen with "Open with…", opens it with the
/// program the operating system assigns to its type — <c>xdg-open</c> or <c>open</c>, the shell's default verb on
/// Windows — outside the application. Registered after <see cref="ExecuteCommand"/>, which takes the programs.
/// </summary>
public class OpenWithSystemDefaultCommand(
    IContentModelService contentModelService,
    FileAssociationRunner runner) : Command
{
    public override string Title => "Open with the system default";
    public override string Description => "Open the selected file with the program the system assigns to its type, outside the application.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    /// <summary>The focused panel's entry; a key the focused panel declined is offered to the other panel too.</summary>
    private Url? FocusedUrl => contentModelService.GetFocusedModel()?.GetFocusedEntry()?.Url;

    private string? FocusedFile => FileAssociationRunner.LocalFilePath(FocusedUrl);

    public override bool CanExecute() =>
        FocusedFile is { } path && !ProgramFile.IsRunnable(path);

    public override Task ExecuteAsync(object? args)
    {
        if (FocusedFile is { } path)
        {
            _ = runner.StartSystemDefault(path);
        }

        return Task.CompletedTask;
    }
}
