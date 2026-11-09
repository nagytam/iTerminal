using System.Text;
using iTerminal.Core.Services.UsersAndGroups;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.UsersAndGroups;

[TestClass]
public class WindowsUserGroupServiceTest
{
    [TestMethod]
    public async Task ParsesLocalUsersAndGroupsFromJson()
    {
        FakeProcessRunner userRunner = new FakeProcessRunner().Answer("powershell.exe", """[{"Name":"ada","FullName":"Ada Lovelace"}]""");
        FakeProcessRunner groupRunner = new FakeProcessRunner().Answer("powershell.exe", """[{"Name":"users"}]""");
        WindowsUserGroupService userService = new(userRunner);
        WindowsUserGroupService groupService = new(groupRunner);

        IReadOnlyList<LocalUser> users = await userService.ListUsersAsync();
        IReadOnlyList<LocalGroup> groups = await groupService.ListGroupsAsync();

        CollectionAssert.AreEqual(new[] { new LocalUser("ada", "Ada Lovelace") }, users.ToArray());
        CollectionAssert.AreEqual(new[] { new LocalGroup("users") }, groups.ToArray());
    }

    [TestMethod]
    public async Task PasswordIsOnlySentOnStandardInput()
    {
        const string password = "not-on-command-line";
        FakeProcessRunner runner = new FakeProcessRunner().Answer("powershell.exe", string.Empty);
        WindowsUserGroupService service = new(runner);

        UserGroupActionResult result = await service.SetPasswordAsync("ada", password);

        FakeProcessRunner.Call call = runner.Calls.Single();
        Assert.IsTrue(result.Success);
        Assert.AreEqual(password + "\n", Encoding.UTF8.GetString(call.StandardInput));
        Assert.IsFalse(call.CommandLine.Contains(password, StringComparison.Ordinal));
        StringAssert.Contains(call.CommandLine, "-Password $securePassword");
        Assert.IsFalse(result.Message.Contains(password, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task QuotesInAccountNamesAreEscapedInPowerShellSource()
    {
        FakeProcessRunner runner = new FakeProcessRunner().Answer("powershell.exe", string.Empty);
        WindowsUserGroupService service = new(runner);

        UserGroupActionResult result = await service.CreateGroupAsync("team's group");

        Assert.IsTrue(result.Success);
        StringAssert.Contains(runner.Calls.Single().CommandLine, "'team''s group'");
    }

    [TestMethod]
    public async Task CreatesUserWithSecurePasswordAndAllowsWindowsGroupNamesWithSpaces()
    {
        FakeProcessRunner runner = new FakeProcessRunner().Answer("powershell.exe", string.Empty);
        WindowsUserGroupService service = new(runner);

        UserGroupActionResult createUser = await service.CreateUserAsync("ada", "Ada Lovelace", "secret");
        UserGroupActionResult createGroup = await service.CreateGroupAsync("Remote Desktop Users");

        Assert.IsTrue(createUser.Success);
        Assert.IsTrue(createGroup.Success);
        StringAssert.Contains(runner.Calls[0].CommandLine, "-Password $securePassword");
        StringAssert.Contains(runner.Calls[1].CommandLine, "'Remote Desktop Users'");
    }

    [TestMethod]
    public async Task AllowsMembershipChangesForWindowsGroupNamesWithSpaces()
    {
        FakeProcessRunner runner = new FakeProcessRunner().Answer("powershell.exe", string.Empty);
        WindowsUserGroupService service = new(runner);

        UserGroupActionResult result = await service.AddMemberAsync("ada", "Remote Desktop Users");

        Assert.IsTrue(result.Success);
        StringAssert.Contains(runner.Calls.Single().CommandLine, "-Group 'Remote Desktop Users'");
    }

    [TestMethod]
    public async Task AcceptsWindowsUserNamesWithSpacesAndUnicode()
    {
        FakeProcessRunner runner = new FakeProcessRunner().Answer("powershell.exe", string.Empty);
        WindowsUserGroupService service = new(runner);

        UserGroupActionResult result = await service.SetPasswordAsync("Zoë Smith", "secret");

        Assert.IsTrue(result.Success);
        StringAssert.Contains(runner.Calls.Single().CommandLine, "'Zoë Smith'");
    }

    [TestMethod]
    public async Task RefusesNewlinePasswordsBeforeStartingAProcess()
    {
        FakeProcessRunner runner = new();
        WindowsUserGroupService service = new(runner);

        UserGroupActionResult result = await service.SetPasswordAsync("ada", "unsafe\r\npassword");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, runner.Calls.Count);
    }
}
