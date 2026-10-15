using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+F12: the properties of the file or folder under the cursor (<see cref="PropertiesDialog"/>).
/// Local disk only — sizes, dates and attributes are read from the disk itself, and the other
/// drives' rows carry no more than the listing already shows — and never the ".." row.
/// </summary>
public class PropertiesDialogCommand(
    PropertiesDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<PropertiesDialog>(dialog)
{
    public override string Title => "Properties";
    public override string Description => "Show the selected entry's size, dates and link target, and change its attributes or permissions.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F12, false, false, true);

    /// <summary>The panel the key acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    private Url? SourceUrl =>
        FocusedEntry.ActionableUrl(SourceModel) is { ContainerType: ContainerType.FileSystem } url ? url : null;

    public override bool CanExecute() => SourceUrl is not null;

    protected override void OnBeforeShow(PropertiesDialog dialog)
    {
        if (SourceUrl is { } url)
        {
            dialog.Load(Path.TrimEndingDirectorySeparator(url.Path));
        }
    }
}
