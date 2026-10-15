using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Core.Commands;

/// <summary>
/// Enter on a file the user has chosen a program for ("Open with…", Shift+F3) opens it with that
/// program. Attached ahead of the panel's other Enter commands and inert unless there is such a
/// choice — so without one Enter does exactly what it always did: opens an archive as a folder,
/// runs the file in the command log.
/// </summary>
public class OpenAssociatedFileCommand(
    IContentModelService contentModelService,
    FileAssociationRunner runner,
    IContentModel? ownModel = null) : Command
{
    public override string Title => "Open with its program";
    public override string Description => "Open the selected file with the program chosen for files like it.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    /// <summary>
    /// The focused panel's entry, and only when that panel is this browser's own: a key the focused
    /// panel declined is offered to the other panel too, which must not then act on it.
    /// </summary>
    private Url? FocusedUrl
    {
        get
        {
            var model = contentModelService?.GetFocusedModel();
            return model is not null && (ownModel is null || ReferenceEquals(model, ownModel))
                ? model.GetFocusedEntry()?.Url
                : null;
        }
    }

    public override bool CanExecute() => runner?.DefaultFor(FocusedUrl, FileAssociationKind.Open) is not null;

    public override Task ExecuteAsync(object? args)
    {
        var url = FocusedUrl;
        if (runner.DefaultFor(url, FileAssociationKind.Open) is { } association
            && FileAssociationRunner.LocalFilePath(url) is { } path)
        {
            _ = runner.Start(association, path);
        }

        return Task.CompletedTask;
    }
}
