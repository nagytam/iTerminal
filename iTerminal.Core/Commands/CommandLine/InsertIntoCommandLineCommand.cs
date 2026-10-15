using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.CommandLine;

/// <summary>What a panel hands to the command line.</summary>
public enum CommandLineInsert
{
    /// <summary>The name of the file or folder under the cursor.</summary>
    FileName,

    /// <summary>The marked entries' names; the one under the cursor when nothing is marked.</summary>
    Marked,

    /// <summary>The folder the panel shows.</summary>
    FolderPath,
}

/// <summary>
/// Copies a name from the panel to the command line, the way mc has Alt+Enter, Ctrl+Enter and
/// Ctrl+X T/P — so a command is composed from what is on screen instead of retyped.
///
/// <para>One class, three kinds, because the only difference is what is inserted. Like every panel
/// command it acts only when its own panel has focus (reference-equal), since a key a focused
/// control declines is offered to every other control too. Names with a blank are quoted
/// (<see cref="TerminalModel.QuoteName"/>). The command line is shown if it was hidden behind the
/// AI bar: text inserted into a line nobody can see is worse than no insertion.</para>
/// </summary>
public class InsertIntoCommandLineCommand(
    CommandLineInsert kind,
    Key key,
    IContentModel ownModel,
    IContentModelService contentModelService,
    TerminalModel terminalModel,
    Action? revealCommandLine = null)
    : Command
{
    public override string Title => kind switch
    {
        CommandLineInsert.FileName => "File name to command line",
        CommandLineInsert.Marked => "Marked files to command line",
        _ => "Folder path to command line",
    };

    public override string Description => kind switch
    {
        CommandLineInsert.FileName => "Copy the name of the file under the cursor to the command line.",
        CommandLineInsert.Marked => "Copy the names of the marked files (or the file under the cursor) to the command line.",
        _ => "Copy the path of the folder this panel shows to the command line.",
    };

    public override Key? Key { get; init; } = key;

    private bool OwnPanelHasFocus => ReferenceEquals(contentModelService.GetFocusedModel(), ownModel);

    /// <summary>What would be inserted now, or null when there is nothing to insert.</summary>
    internal string? Text()
    {
        switch (kind)
        {
            case CommandLineInsert.FileName:
                return NameOf(ownModel.GetFocusedEntry());
            case CommandLineInsert.Marked:
                {
                    if (ownModel is IMarkableContentModel marks
                        && marks.GetMarkedEntries().Select(NameOf).OfType<string>().ToList() is { Count: > 0 } names)
                    {
                        return string.Join(' ', names);
                    }

                    return NameOf(ownModel.GetFocusedEntry());
                }
            default:
                {
                    var path = ownModel.Url is { ContainerType: ContainerType.FileSystem } url
                        ? Path.TrimEndingDirectorySeparator(url.Path)
                        : ownModel.Url?.FullUrl;
                    return string.IsNullOrEmpty(path) ? null : TerminalModel.QuoteName(path);
                }
        }
    }

    private static string? NameOf(IEntry? entry)
    {
        if (entry is null || FocusedEntry.IsParentEntry(entry) || entry is IDriveObjectEntry)
        {
            return null;
        }

        var name = FocusedEntry.NameOf(entry.Url);
        return name.Length == 0 ? null : TerminalModel.QuoteName(name);
    }

    public override bool CanExecute() => OwnPanelHasFocus && Text() is not null && !terminalModel.IsRunning;

    public override Task ExecuteAsync(object? arg)
    {
        if (Text() is { } text && terminalModel.InsertText(text))
        {
            revealCommandLine?.Invoke();
        }

        return Task.CompletedTask;
    }
}
