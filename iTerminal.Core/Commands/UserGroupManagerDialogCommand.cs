using iConsole.Data;
using iTerminal.Core.Dialogs.UsersAndGroups;

namespace iTerminal.Core.Commands;

public sealed class UserGroupManagerDialogCommand(UserGroupManagerDialog userGroupManagerDialog)
    : ShowDialogCommand<UserGroupManagerDialog>(userGroupManagerDialog)
{
    public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    public override string Title => iConsole.Localization.UiText.Get("Dialog.UserGroupTitle", "User and group manager");
    public override string Description => "Manage local users, groups, passwords and group membership.";

    public override bool CanExecute() => IsSupported && base.CanExecute();
}
