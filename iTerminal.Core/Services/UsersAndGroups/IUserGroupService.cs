namespace iTerminal.Core.Services.UsersAndGroups;

public interface IUserGroupService
{
    Task<IReadOnlyList<LocalUser>> ListUsersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalGroup>> ListGroupsAsync(CancellationToken cancellationToken = default);
    Task<UserGroupActionResult> CreateUserAsync(string name, string fullName, string password, CancellationToken cancellationToken = default);
    Task<UserGroupActionResult> UpdateUserAsync(string name, string fullName, CancellationToken cancellationToken = default);
    Task<UserGroupActionResult> DeleteUserAsync(string name, CancellationToken cancellationToken = default);
    Task<UserGroupActionResult> SetPasswordAsync(string name, string password, CancellationToken cancellationToken = default);
    Task<UserGroupActionResult> CreateGroupAsync(string name, CancellationToken cancellationToken = default);
    Task<UserGroupActionResult> RenameGroupAsync(string oldName, string newName, CancellationToken cancellationToken = default);
    Task<UserGroupActionResult> DeleteGroupAsync(string name, CancellationToken cancellationToken = default);
    Task<UserGroupActionResult> AddMemberAsync(string userName, string groupName, CancellationToken cancellationToken = default);
    Task<UserGroupActionResult> RemoveMemberAsync(string userName, string groupName, CancellationToken cancellationToken = default);
}
