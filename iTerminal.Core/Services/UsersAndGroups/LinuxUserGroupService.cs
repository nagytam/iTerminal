using System.Text;
using System.Text.RegularExpressions;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.UsersAndGroups;

public sealed class LinuxUserGroupService(IExternalProcessRunner runner) : IUserGroupService
{
    private static readonly Regex AccountNamePattern = new("^[A-Za-z_][A-Za-z0-9_.-]*[$]?$", RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<LocalUser>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        ProcessOutput output = await runner.RunAsync("getent", ["-s", "files", "passwd"], cancellationToken: cancellationToken);
        EnsureSuccess(output, "Could not list local users");
        return output.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':'))
            .Where(fields => fields.Length >= 5)
            .Select(fields => new LocalUser(fields[0], fields[4]))
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalGroup>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        ProcessOutput output = await runner.RunAsync("getent", ["-s", "files", "group"], cancellationToken: cancellationToken);
        EnsureSuccess(output, "Could not list local groups");
        return output.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':'))
            .Where(fields => fields.Length >= 4)
            .Select(fields => new LocalGroup(fields[0]))
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<UserGroupActionResult> CreateUserAsync(string name, string fullName, string password, CancellationToken cancellationToken = default)
    {
        if (!ValidName(name) || !ValidText(fullName) || !ValidPassword(password))
        {
            return InvalidInput();
        }

        ProcessOutput create = await runner.RunAsync("useradd", ["-m", "-c", fullName, "--", name], cancellationToken: cancellationToken);
        if (!create.Success)
        {
            return Failure(create);
        }

        ProcessOutput setPassword = await RunPasswordAsync(name, password, cancellationToken);
        if (setPassword.Success)
        {
            return Success($"{name} was created.");
        }

        ProcessOutput remove = await runner.RunAsync("userdel", ["-r", "--", name], cancellationToken: cancellationToken);
        return Failure(setPassword, remove.Success
            ? "The password could not be set; the new account was removed."
            : "The password could not be set and the new account could not be removed.");
    }

    public Task<UserGroupActionResult> UpdateUserAsync(string name, string fullName, CancellationToken cancellationToken = default) =>
        RunAccountCommandAsync("usermod", ["-c", fullName, "--", name], name, fullName, cancellationToken);

    public Task<UserGroupActionResult> DeleteUserAsync(string name, CancellationToken cancellationToken = default) =>
        RunNamedCommandAsync("userdel", ["-r", "--", name], name, cancellationToken);

    public async Task<UserGroupActionResult> SetPasswordAsync(string name, string password, CancellationToken cancellationToken = default)
    {
        if (!ValidName(name) || !ValidPassword(password))
        {
            return InvalidInput();
        }

        ProcessOutput output = await RunPasswordAsync(name, password, cancellationToken);
        return output.Success ? Success($"The password for {name} was changed.") : Failure(output);
    }

    public Task<UserGroupActionResult> CreateGroupAsync(string name, CancellationToken cancellationToken = default) =>
        RunNamedCommandAsync("groupadd", ["--", name], name, cancellationToken);

    public Task<UserGroupActionResult> DeleteGroupAsync(string name, CancellationToken cancellationToken = default) =>
        RunNamedCommandAsync("groupdel", ["--", name], name, cancellationToken);

    public async Task<UserGroupActionResult> RenameGroupAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        if (!ValidName(oldName) || !ValidName(newName))
        {
            return InvalidInput();
        }

        ProcessOutput output = await runner.RunAsync("groupmod", ["-n", newName, "--", oldName], cancellationToken: cancellationToken);
        return output.Success ? Success($"{oldName} was renamed to {newName}.") : Failure(output);
    }

    public Task<UserGroupActionResult> AddMemberAsync(string userName, string groupName, CancellationToken cancellationToken = default) =>
        RunNamesCommandAsync("usermod", ["-a", "-G", groupName, "--", userName], userName, groupName, cancellationToken);

    public Task<UserGroupActionResult> RemoveMemberAsync(string userName, string groupName, CancellationToken cancellationToken = default) =>
        RunNamesCommandAsync("gpasswd", ["-d", userName, groupName], userName, groupName, cancellationToken);

    private async Task<UserGroupActionResult> RunAccountCommandAsync(string executable, IReadOnlyList<string> arguments, string name, string fullName, CancellationToken cancellationToken)
    {
        if (!ValidName(name) || !ValidText(fullName))
        {
            return InvalidInput();
        }

        ProcessOutput output = await runner.RunAsync(executable, arguments, cancellationToken: cancellationToken);
        return output.Success ? Success($"{name} was updated.") : Failure(output);
    }

    private async Task<UserGroupActionResult> RunNamedCommandAsync(string executable, IReadOnlyList<string> arguments, string name, CancellationToken cancellationToken)
    {
        if (!ValidName(name))
        {
            return InvalidInput();
        }

        ProcessOutput output = await runner.RunAsync(executable, arguments, cancellationToken: cancellationToken);
        return output.Success ? Success($"{name} was changed.") : Failure(output);
    }

    private async Task<UserGroupActionResult> RunNamesCommandAsync(string executable, IReadOnlyList<string> arguments, string userName, string groupName, CancellationToken cancellationToken)
    {
        if (!ValidName(userName) || !ValidName(groupName))
        {
            return InvalidInput();
        }

        ProcessOutput output = await runner.RunAsync(executable, arguments, cancellationToken: cancellationToken);
        return output.Success ? Success($"{userName} was changed in {groupName}.") : Failure(output);
    }

    private Task<ProcessOutput> RunPasswordAsync(string name, string password, CancellationToken cancellationToken) =>
        runner.RunWithStandardInputAsync(
            "chpasswd",
            [],
            async stream =>
            {
                byte[] input = Encoding.UTF8.GetBytes($"{name}:{password}\n");
                await stream.WriteAsync(input, cancellationToken);
                Array.Clear(input);
            },
            cancellationToken: cancellationToken);

    private static bool ValidName(string value) => AccountNamePattern.IsMatch(value);

    private static bool ValidText(string value) => !value.Any(char.IsControl);

    private static bool ValidPassword(string value) => value.Length > 0 && !value.Any(c => c is '\r' or '\n' or '\0');

    private static UserGroupActionResult InvalidInput() => new(false, "The account name, full name or password is not valid.");

    private static UserGroupActionResult Success(string message) => new(true, message);

    private static UserGroupActionResult Failure(ProcessOutput output, string? message = null) =>
        new(false, message ?? (output.ExitCode == 1 && output.StandardError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            ? "Permission was denied. Restart iTerminal with suitable privileges."
            : output.FirstErrorLine));

    private static void EnsureSuccess(ProcessOutput output, string message)
    {
        if (!output.Success)
        {
            throw new InvalidOperationException(output.ExitCode == 1 && output.StandardError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
                ? "Permission was denied while listing local accounts."
                : $"{message}: {output.FirstErrorLine}");
        }
    }
}
