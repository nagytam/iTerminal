using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.UserMenu;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+F10: the user menu (<see cref="UserMenuDialog"/>), mc's F2. F2 is the terminal here, so
/// the menu lives next to the other Shift+F-keys. Offered on a local folder only: the entries are
/// scripts, and they need a folder on this disk to run in.
/// </summary>
public class UserMenuDialogCommand(
    UserMenuDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<UserMenuDialog>(dialog)
{
    public override string Title => "User menu";
    public override string Description => "Run one of your own commands on the file under the cursor (the F2 menu of mc).";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F10, true, false, false);

    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    private static bool IsLocalFolder(IContentModel? model) =>
        model?.Url is { ContainerType: ContainerType.FileSystem } url && Directory.Exists(url.Path);

    public override bool CanExecute() => IsLocalFolder(SourceModel);

    protected override void OnBeforeShow(UserMenuDialog dialog)
    {
        var model = SourceModel;
        if (model is null)
        {
            return;
        }

        var entry = model.GetFocusedEntry();
        var current = entry is not null && !FocusedEntry.IsParentEntry(entry) ? FocusedEntry.NameOf(entry.Url) : string.Empty;
        IReadOnlyList<string> marked = model is IMarkableContentModel marks && marks.GetMarkedEntries() is { Count: > 0 } rows
            ? [.. rows.Where(e => !FocusedEntry.IsParentEntry(e)).Select(e => FocusedEntry.NameOf(e.Url))]
            : [];
        var other = contentModelService?.NonFocusedFolderUrl;
        var otherFolder = other is { ContainerType: ContainerType.FileSystem } ? other.Path : string.Empty;
        dialog.Load(new UserMenuContext(current, Path.TrimEndingDirectorySeparator(model.Url.Path), otherFolder, marked));
    }
}
