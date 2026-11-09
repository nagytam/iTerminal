using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Prompts;
using iConsole.Services;
using iTerminal.Core.Services.UsersAndGroups;

namespace iTerminal.Core.Dialogs.UsersAndGroups;

public sealed class UserGroupManagerDialog : Dialog
{
    private enum Listing
    {
        Users,
        Groups,
    }

    private const int DialogWidth = 78;
    private const int DialogHeight = 24;
    private readonly object _sync = new();
    private IReadOnlyList<LocalUser> _users = [];
    private IReadOnlyList<LocalGroup> _groups = [];
    private Listing _listing;
    private int _selectedIndex;
    private int _firstVisibleIndex;
    private string _filter = string.Empty;
    private string _status = string.Empty;
    private bool _filtering;
    private bool _busy;
    private bool _statusIsError;
    private long _generation;
    private Outcome? _outcome;
    private readonly IUserGroupService _userGroupService;
    private readonly IPromptService _promptService;

    public UserGroupManagerDialog(
        IUserGroupService userGroupService,
        IPromptService promptService,
        IThemeService themeService)
        : base(nameof(UserGroupManagerDialog), themeService)
    {
        _userGroupService = userGroupService;
        _promptService = promptService;
        Title = UiText.Get("Dialog.UserGroupTitle", "User and group manager");
    }

    private sealed record Outcome(
        long Generation,
        IReadOnlyList<LocalUser>? Users,
        IReadOnlyList<LocalGroup>? Groups,
        string Message,
        bool IsError,
        bool Busy);

    public override Task ShowAsync()
    {
        _generation++;
        _listing = Listing.Users;
        _selectedIndex = 0;
        _firstVisibleIndex = 0;
        _filter = string.Empty;
        _filtering = false;
        _status = UiText.Get("UserGroup.Loading", "Loading local accounts...");
        _statusIsError = false;
        _busy = true;
        StartLoad(_generation);
        return base.ShowAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, DialogHeight));
        }

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_filtering)
        {
            yield return ("Type", UiText.Get("UserGroup.HintFilter", "Filter the list"));
            yield return ("Enter", UiText.Get("UserGroup.HintKeepFilter", "Keep the filter"));
            yield return ("Esc", UiText.Get("UserGroup.HintClearFilter", "Clear the filter"));
            yield break;
        }

        yield return ("Tab", UiText.Get("UserGroup.HintSwitch", "Switch between users and groups"));
        yield return ("N", UiText.Get("UserGroup.HintAdd", "Add a user or group"));
        yield return ("E", UiText.Get("UserGroup.HintEdit", "Edit the selected user or group"));
        yield return ("D", UiText.Get("UserGroup.HintDelete", "Delete the selected account or group"));
        if (_listing == Listing.Users)
        {
            yield return ("P", UiText.Get("UserGroup.HintPassword", "Set the user's password"));
            yield return ("A/R", UiText.Get("UserGroup.HintMembership", "Add or remove group membership"));
        }
        yield return ("/", UiText.Get("UserGroup.HintFilter", "Filter the list"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_filtering)
        {
            return HandleFilterKey(keyEvent);
        }

        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        if (plain && keyEvent.ConsoleKey == ConsoleKey.Tab)
        {
            _listing = _listing == Listing.Users ? Listing.Groups : Listing.Users;
            _filter = string.Empty;
            _selectedIndex = 0;
            _firstVisibleIndex = 0;
            StateHasChanged();
            return true;
        }

        if (plain && keyEvent.KeyChar == '/')
        {
            _filtering = true;
            StateHasChanged();
            return true;
        }

        if (plain)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    MoveSelection(-1);
                    return true;
                case ConsoleKey.DownArrow:
                    MoveSelection(1);
                    return true;
                case ConsoleKey.Home:
                    _selectedIndex = 0;
                    _firstVisibleIndex = 0;
                    StateHasChanged();
                    return true;
                case ConsoleKey.End:
                    _selectedIndex = Math.Max(0, VisibleCount - 1);
                    EnsureSelectionVisible(ListRowCount);
                    StateHasChanged();
                    return true;
                case ConsoleKey.Insert:
                    if (!_busy) _ = CreateAsync();
                    return true;
                case ConsoleKey.Delete:
                    if (!_busy) _ = DeleteAsync();
                    return true;
            }

            switch (char.ToLowerInvariant(keyEvent.KeyChar))
            {
                case 'n':
                    if (!_busy) _ = CreateAsync();
                    return true;
                case 'e':
                    if (!_busy)
                    {
                        if (_listing == Listing.Users && SelectedUser is { } user) _ = EditUserAsync(user);
                        else if (_listing == Listing.Groups && SelectedGroup is { } group) _ = RenameGroupAsync(group);
                    }
                    return true;
                case 'd':
                    if (!_busy) _ = DeleteAsync();
                    return true;
                case 'p':
                    if (!_busy && _listing == Listing.Users && SelectedUser is { } selectedUser) _ = SetPasswordAsync(selectedUser);
                    return true;
                case 'a':
                    if (!_busy && _listing == Listing.Users && SelectedUser is { } addUser) _ = ChangeMembershipAsync(addUser, add: true);
                    return true;
                case 'r':
                    if (!_busy && _listing == Listing.Users && SelectedUser is { } removeUser) _ = ChangeMembershipAsync(removeUser, add: false);
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private int VisibleCount => _listing == Listing.Users ? VisibleUsers.Count : VisibleGroups.Count;

    private IReadOnlyList<LocalUser> VisibleUsers =>
        _users.Where(user => Matches(user.Name, user.FullName)).ToArray();

    private IReadOnlyList<LocalGroup> VisibleGroups =>
        _groups.Where(group => Matches(group.Name)).ToArray();

    private LocalUser? SelectedUser => VisibleUsers.ElementAtOrDefault(_selectedIndex);

    private LocalGroup? SelectedGroup => VisibleGroups.ElementAtOrDefault(_selectedIndex);

    private bool HandleFilterKey(KeyEvent keyEvent)
    {
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _filter = string.Empty;
                _filtering = false;
                _selectedIndex = 0;
                _firstVisibleIndex = 0;
                StateHasChanged();
                return true;
            case ConsoleKey.Enter:
                _filtering = false;
                StateHasChanged();
                return true;
            case ConsoleKey.Backspace when _filter.Length > 0:
                _filter = _filter[..^1];
                _selectedIndex = 0;
                _firstVisibleIndex = 0;
                StateHasChanged();
                return true;
        }

        if (keyEvent.KeyChar >= ' ' && !keyEvent.Control && !keyEvent.Alt && _filter.Length < 120)
        {
            _filter += keyEvent.KeyChar;
            _selectedIndex = 0;
            _firstVisibleIndex = 0;
            StateHasChanged();
        }

        return true;
    }

    private void MoveSelection(int delta)
    {
        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, Math.Max(0, VisibleCount - 1));
        EnsureSelectionVisible(ListRowCount);
        StateHasChanged();
    }

    private int ListRowCount => Math.Max(1, (int)ViewSize.Height - 8);

    private void EnsureSelectionVisible(int rows)
    {
        if (_selectedIndex < _firstVisibleIndex)
        {
            _firstVisibleIndex = _selectedIndex;
        }
        else if (_selectedIndex >= _firstVisibleIndex + rows)
        {
            _firstVisibleIndex = _selectedIndex - rows + 1;
        }
    }

    private async Task CreateAsync()
    {
        if (_listing == Listing.Groups)
        {
            string? groupName = await AskText(UiText.Get("UserGroup.GroupName", "Group name"));
            if (groupName is not null)
            {
                StartOperation(() => _userGroupService.CreateGroupAsync(groupName));
            }
            return;
        }

        string? name = await AskText(UiText.Get("UserGroup.UserName", "User name"));
        if (name is null) return;
        string? fullName = await AskText(UiText.Get("UserGroup.FullName", "Full name"));
        if (fullName is null) return;
        string? password = await AskPassword();
        if (password is not null)
        {
            StartOperation(() => _userGroupService.CreateUserAsync(name, fullName, password));
        }
    }

    private async Task EditUserAsync(LocalUser user)
    {
        string? fullName = await AskText(string.Format(UiText.Get("UserGroup.EditFullName", "Full name for {0}"), user.FullName), user.FullName);
        if (fullName is not null)
        {
            StartOperation(() => _userGroupService.UpdateUserAsync(user.Name, fullName));
        }
    }

    private async Task RenameGroupAsync(LocalGroup group)
    {
        string question = string.Format(UiText.Get("UserGroup.RenameGroup", "New name for {0}"), group.Name);
        string? newName = await AskText(question);
        if (newName is not null)
        {
            StartOperation(() => _userGroupService.RenameGroupAsync(group.Name, newName));
        }
    }

    private async Task SetPasswordAsync(LocalUser user)
    {
        string? password = await AskPassword();
        if (password is not null)
        {
            StartOperation(() => _userGroupService.SetPasswordAsync(user.Name, password));
        }
    }

    private async Task ChangeMembershipAsync(LocalUser user, bool add)
    {
        string? groupName = await AskText(UiText.Get("UserGroup.GroupName", "Group name"));
        if (groupName is null) return;
        StartOperation(() => add
            ? _userGroupService.AddMemberAsync(user.Name, groupName)
            : _userGroupService.RemoveMemberAsync(user.Name, groupName));
    }

    private async Task DeleteAsync()
    {
        string name = _listing == Listing.Users ? SelectedUser?.Name ?? string.Empty : SelectedGroup?.Name ?? string.Empty;
        if (name.Length == 0) return;

        Listing listing = _listing;
        bool? confirmed = await _promptService.ConfirmAsync(new ConfirmationPrompt(
            string.Format(UiText.Get("UserGroup.DeleteConfirm", "Delete {0} \"{1}\"?"), listing == Listing.Users ? "user" : "group", name)));
        if (confirmed != true) return;

        StartOperation(() => listing == Listing.Users
            ? _userGroupService.DeleteUserAsync(name)
            : _userGroupService.DeleteGroupAsync(name));
    }

    private Task<string?> AskText(string question, string? defaultValue = null)
    {
        TextPrompt<string> prompt = new(question) { DefaultValue = defaultValue };
        prompt.Validate(value => value.Any(char.IsControl) ? UiText.Get("UserGroup.InvalidText", "Control characters are not allowed.") : null);
        return _promptService.AskAsync(prompt);
    }

    private Task<string?> AskPassword()
    {
        TextPrompt<string> prompt = new(UiText.Get("UserGroup.Password", "Password")) { IsSecret = true };
        prompt.Validate(value => value.Any(c => c is '\r' or '\n' or '\0')
            ? UiText.Get("UserGroup.InvalidPassword", "Password must not contain a line break.")
            : null);
        return _promptService.AskAsync(prompt);
    }

    private void StartLoad(long generation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                IReadOnlyList<LocalUser> users = await _userGroupService.ListUsersAsync();
                IReadOnlyList<LocalGroup> groups = await _userGroupService.ListGroupsAsync();
                Park(new Outcome(generation, users, groups, string.Empty, false, false));
            }
            catch (Exception exception)
            {
                Park(new Outcome(generation, null, null, exception.Message, true, false));
            }
        });
    }

    private void StartOperation(Func<Task<UserGroupActionResult>> operation)
    {
        long generation = _generation;
        Park(new Outcome(generation, null, null, UiText.Get("UserGroup.Working", "Applying account change..."), false, true));
        _ = Task.Run(async () =>
        {
            try
            {
                UserGroupActionResult result = await operation();
                IReadOnlyList<LocalUser>? users = null;
                IReadOnlyList<LocalGroup>? groups = null;
                string message = result.Message;
                bool error = !result.Success;
                if (result.Success)
                {
                    users = await _userGroupService.ListUsersAsync();
                    groups = await _userGroupService.ListGroupsAsync();
                }
                Park(new Outcome(generation, users, groups, message, error, false));
            }
            catch (Exception exception)
            {
                Park(new Outcome(generation, null, null, exception.Message, true, false));
            }
        });
    }

    private void Park(Outcome outcome)
    {
        lock (_sync)
        {
            _outcome = outcome;
        }
    }

    public override void OnIdle()
    {
        base.OnIdle();
        Outcome? outcome;
        lock (_sync)
        {
            outcome = _outcome;
            _outcome = null;
        }

        if (outcome is null || outcome.Generation != _generation)
        {
            return;
        }

        if (outcome.Users is not null) _users = outcome.Users;
        if (outcome.Groups is not null) _groups = outcome.Groups;
        _status = outcome.Message;
        _statusIsError = outcome.IsError;
        _busy = outcome.Busy;
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, VisibleCount - 1));
        EnsureSelectionVisible(ListRowCount);
        StateHasChanged();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        Color foreground = ThemeService.CurrentColorScheme.Foreground;
        Color background = ThemeService.CurrentColorScheme.Background;
        Color hint = ThemeService.CurrentColorScheme.DisabledForeground;
        int width = Math.Max(1, (int)ViewSize.Width - 4);
        int rows = Math.Max(1, (int)ViewSize.Height - 8);
        WriteRow(buffer, 0, _listing == Listing.Users
            ? UiText.Get("UserGroup.UsersTitle", "Local users  [Tab: groups]")
            : UiText.Get("UserGroup.GroupsTitle", "Local groups  [Tab: users]"), foreground, background, width);
        WriteRow(buffer, 1, $"{UiText.Get("UserGroup.Filter", "Filter")}: {_filter}", foreground, background, width);

        IReadOnlyList<string> items = _listing == Listing.Users
            ? VisibleUsers.Select(user => $"{user.Name}  {user.FullName}").ToArray()
            : VisibleGroups.Select(group => group.Name).ToArray();
        for (int row = 0; row < rows; row++)
        {
            int index = _firstVisibleIndex + row;
            string text = index < items.Count
                ? (index == _selectedIndex ? "› " : "  ") + items[index]
                : index == 0 && items.Count == 0
                    ? _filter.Length > 0
                        ? UiText.Get("UserGroup.NoMatches", "No accounts match the filter.")
                        : _listing == Listing.Users
                            ? UiText.Get("UserGroup.NoUsers", "No local users.")
                            : UiText.Get("UserGroup.NoGroups", "No local groups.")
                    : string.Empty;
            Color rowForeground = index == _selectedIndex && index < items.Count
                ? ThemeService.CurrentColorScheme.SelectedFocusedForeground
                : hint;
            Color rowBackground = index == _selectedIndex && index < items.Count
                ? ThemeService.CurrentColorScheme.SelectedFocusedBackground
                : background;
            WriteRow(buffer, row + 2, text, rowForeground, rowBackground, width);
        }

        WriteRow(buffer, rows + 2, _status, _statusIsError ? ThemeService.CurrentColorScheme.Error : foreground, background, width);
        WriteRow(buffer, rows + 3, _busy
            ? UiText.Get("UserGroup.Busy", "Please wait...")
            : UiText.Get("UserGroup.HintLine", "N: add  E: edit user  D: delete  P: password  A/R: membership  /: filter"),
            hint, background, width);
    }

    private void WriteRow(IBuffer buffer, int row, string text, Color foreground, Color background, int width, bool isBold = false)
    {
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 2, AbsolutePosition.Y + 1 + row),
            new Size(width, 1),
            new ConxelString(TextWidth.Fit(text, width), foreground, background, isBold));
    }

    private bool Matches(params string[] values) =>
        _filter.Length == 0 || values.Any(value => value.Contains(_filter, StringComparison.OrdinalIgnoreCase));

    internal void ApplyPendingForTest() => OnIdle();
    internal bool IsBusyForTest => _busy;
    internal bool IsShowingGroupsForTest => _listing == Listing.Groups;
    internal IReadOnlyList<LocalUser> VisibleUsersForTest => VisibleUsers;
    internal IReadOnlyList<LocalGroup> VisibleGroupsForTest => VisibleGroups;
    internal string StatusForTest => _status;
}
