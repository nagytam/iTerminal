using System.Text;
using System.Text.Json;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.UsersAndGroups;

public sealed class WindowsUserGroupService(IExternalProcessRunner runner) : IUserGroupService
{
    private const string PowerShell = "powershell.exe";
    private static bool ValidGroupName(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl) && !value.Contains('/') && !value.Contains(',');

    public async Task<IReadOnlyList<LocalUser>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        ProcessOutput output = await RunScriptAsync(
            "ConvertTo-Json -Compress -InputObject @(Get-LocalUser | Select-Object Name, FullName)",
            cancellationToken);
        EnsureSuccess(output, "Could not list local users");
        using JsonDocument document = JsonDocument.Parse(output.StandardOutput);
        return ReadArray(document.RootElement)
            .Select(item => new LocalUser(ReadString(item, "Name"), ReadString(item, "FullName")))
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalGroup>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        ProcessOutput output = await RunScriptAsync(
            "ConvertTo-Json -Compress -InputObject @(Get-LocalGroup | Select-Object Name)",
            cancellationToken);
        EnsureSuccess(output, "Could not list local groups");
        using JsonDocument document = JsonDocument.Parse(output.StandardOutput);
        return ReadArray(document.RootElement)
            .Select(item => new LocalGroup(ReadString(item, "Name")))
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<UserGroupActionResult> CreateUserAsync(string name, string fullName, string password, CancellationToken cancellationToken = default)
    {
        if (!ValidName(name) || !ValidText(fullName) || !ValidPassword(password))
        {
            return Task.FromResult(InvalidInput());
        }

        string script = $"$password = [Console]::In.ReadLine(); if ($null -eq $password) {{ throw 'Password input was not received.' }}; " +
            $"$securePassword = ConvertTo-SecureString $password -AsPlainText -Force; " +
            $"New-LocalUser -Name {Literal(name)} -FullName {Literal(fullName)} -Password $securePassword | Out-Null";
        return RunPasswordScriptAsync(script, password, $"{name} was created.", cancellationToken);
    }

    public Task<UserGroupActionResult> UpdateUserAsync(string name, string fullName, CancellationToken cancellationToken = default) =>
        RunCommandAsync($"Set-LocalUser -Name {Literal(name)} -FullName {Literal(fullName)}", name, fullName, cancellationToken);

    public Task<UserGroupActionResult> DeleteUserAsync(string name, CancellationToken cancellationToken = default) =>
        RunNamedCommandAsync($"Remove-LocalUser -Name {Literal(name)}", name, cancellationToken);

    public Task<UserGroupActionResult> SetPasswordAsync(string name, string password, CancellationToken cancellationToken = default)
    {
        if (!ValidName(name) || !ValidPassword(password))
        {
            return Task.FromResult(InvalidInput());
        }

        string script = $"$password = [Console]::In.ReadLine(); if ($null -eq $password) {{ throw 'Password input was not received.' }}; " +
            $"$securePassword = ConvertTo-SecureString $password -AsPlainText -Force; " +
            $"Set-LocalUser -Name {Literal(name)} -Password $securePassword";
        return RunPasswordScriptAsync(script, password, $"The password for {name} was changed.", cancellationToken);
    }

    public Task<UserGroupActionResult> CreateGroupAsync(string name, CancellationToken cancellationToken = default) =>
        RunGroupCommandAsync($"New-LocalGroup -Name {Literal(name)} | Out-Null", name, cancellationToken);

    public Task<UserGroupActionResult> DeleteGroupAsync(string name, CancellationToken cancellationToken = default) =>
        RunGroupCommandAsync($"Remove-LocalGroup -Name {Literal(name)}", name, cancellationToken);

    public async Task<UserGroupActionResult> RenameGroupAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        if (!ValidGroupName(oldName) || !ValidGroupName(newName))
        {
            return InvalidInput();
        }

        string script = $"([ADSI]({Literal($"WinNT://./{oldName},group")})).Rename({Literal(newName)})";
        ProcessOutput output = await RunScriptAsync(script, cancellationToken);
        return output.Success ? Success($"{oldName} was renamed to {newName}.") : Failure(output);
    }

    public Task<UserGroupActionResult> AddMemberAsync(string userName, string groupName, CancellationToken cancellationToken = default) =>
        RunGroupMembershipCommandAsync($"Add-LocalGroupMember -Group {Literal(groupName)} -Member {Literal(userName)}", userName, groupName, cancellationToken);

    public Task<UserGroupActionResult> RemoveMemberAsync(string userName, string groupName, CancellationToken cancellationToken = default) =>
        RunGroupMembershipCommandAsync($"Remove-LocalGroupMember -Group {Literal(groupName)} -Member {Literal(userName)}", userName, groupName, cancellationToken);

    private async Task<UserGroupActionResult> RunCommandAsync(string script, string name, string fullName, CancellationToken cancellationToken)
    {
        if (!ValidName(name) || !ValidText(fullName))
        {
            return InvalidInput();
        }

        ProcessOutput output = await RunScriptAsync(script, cancellationToken);
        return output.Success ? Success($"{name} was updated.") : Failure(output);
    }

    private async Task<UserGroupActionResult> RunNamedCommandAsync(string script, string name, CancellationToken cancellationToken)
    {
        if (!ValidName(name))
        {
            return InvalidInput();
        }

        ProcessOutput output = await RunScriptAsync(script, cancellationToken);
        return output.Success ? Success($"{name} was changed.") : Failure(output);
    }

    private async Task<UserGroupActionResult> RunGroupCommandAsync(string script, string name, CancellationToken cancellationToken)
    {
        if (!ValidGroupName(name))
        {
            return InvalidInput();
        }

        ProcessOutput output = await RunScriptAsync(script, cancellationToken);
        return output.Success ? Success($"{name} was changed.") : Failure(output);
    }

    private async Task<UserGroupActionResult> RunGroupMembershipCommandAsync(string script, string userName, string groupName, CancellationToken cancellationToken)
    {
        if (!ValidName(userName) || !ValidGroupName(groupName))
        {
            return InvalidInput();
        }

        ProcessOutput output = await RunScriptAsync(script, cancellationToken);
        return output.Success ? Success($"{userName} was changed in {groupName}.") : Failure(output);
    }

    private async Task<UserGroupActionResult> RunPasswordScriptAsync(string script, string password, string successMessage, CancellationToken cancellationToken)
    {
        ProcessOutput output = await runner.RunWithStandardInputAsync(
            PowerShell,
            Arguments(script),
            async stream =>
            {
                byte[] input = Encoding.UTF8.GetBytes(password + "\n");
                await stream.WriteAsync(input, cancellationToken);
                Array.Clear(input);
            },
            cancellationToken: cancellationToken);
        return output.Success ? Success(successMessage) : Failure(output);
    }

    private Task<ProcessOutput> RunScriptAsync(string script, CancellationToken cancellationToken) =>
        runner.RunAsync(PowerShell, Arguments(script), cancellationToken: cancellationToken);

    private static IReadOnlyList<string> Arguments(string script) =>
        ["-NoProfile", "-NonInteractive", "-Command", Wrap(script)];

    private static string Wrap(string script) =>
        "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
        "[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false); " +
        "$ErrorActionPreference = 'Stop'; try { " + script +
        " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static IEnumerable<JsonElement> ReadArray(JsonElement root) =>
        root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToArray(),
            JsonValueKind.Object => [root],
            JsonValueKind.Null => [],
            _ => throw new InvalidOperationException("The account command returned an unexpected response."),
        };

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool ValidName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 20
        && value[^1] != '.'
        && !value.Any(char.IsControl)
        && !value.Any(character => "\"/\\[]:;|=,+*?<>".IndexOf(character) >= 0);

    private static bool ValidText(string value) => !value.Any(char.IsControl);

    private static bool ValidPassword(string value) => value.Length > 0 && !value.Any(c => c is '\r' or '\n' or '\0');

    private static UserGroupActionResult InvalidInput() => new(false, "The account name, full name or password is not valid.");

    private static UserGroupActionResult Success(string message) => new(true, message);

    private static UserGroupActionResult Failure(ProcessOutput output) =>
        new(false, output.StandardError.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            ? "Permission was denied. Restart iTerminal with suitable privileges."
            : output.FirstErrorLine);

    private static void EnsureSuccess(ProcessOutput output, string message)
    {
        if (!output.Success)
        {
            throw new InvalidOperationException(output.StandardError.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
                ? "Permission was denied while listing local accounts."
                : $"{message}: {output.FirstErrorLine}");
        }
    }
}
