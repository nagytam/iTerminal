using System.Text;
using iTerminal.Core.Services.UsersAndGroups;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.UsersAndGroups;

[TestClass]
public class LinuxUserGroupServiceTest
{
    [TestMethod]
    public async Task ListsLocalUsersAndGroups()
    {
        FakeProcessRunner runner = new FakeProcessRunner()
            .Answer("getent -s files passwd", "ada:x:1001:1001:Ada Lovelace:/home/ada:/bin/bash\n")
            .Answer("getent -s files group", "users:x:100:\n");
        LinuxUserGroupService service = new(runner);

        IReadOnlyList<LocalUser> users = await service.ListUsersAsync();
        IReadOnlyList<LocalGroup> groups = await service.ListGroupsAsync();

        CollectionAssert.AreEqual(new[] { new LocalUser("ada", "Ada Lovelace") }, users.ToArray());
        CollectionAssert.AreEqual(new[] { new LocalGroup("users") }, groups.ToArray());
    }

    [TestMethod]
    public async Task CreateAndSetPasswordSendTheSecretOnlyOnStandardInput()
    {
        const string password = "p@ssword:with-colon";
        FakeProcessRunner runner = new FakeProcessRunner()
            .Answer("useradd ", string.Empty)
            .Answer("chpasswd", string.Empty);
        LinuxUserGroupService service = new(runner);

        UserGroupActionResult result = await service.CreateUserAsync("ada", "Ada Lovelace", password);

        Assert.IsTrue(result.Success);
        FakeProcessRunner.Call passwordCall = runner.Calls.Single(call => call.FileName == "chpasswd");
        Assert.AreEqual("ada:" + password + "\n", Encoding.UTF8.GetString(passwordCall.StandardInput));
        Assert.IsFalse(passwordCall.CommandLine.Contains(password, StringComparison.Ordinal));
        Assert.IsFalse(result.Message.Contains(password, StringComparison.Ordinal));
        Assert.IsFalse(runner.Calls.Any(call => call.CommandLine.Contains(password, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RefusesNewlinePasswordsBeforeStartingAProcess()
    {
        FakeProcessRunner runner = new();
        LinuxUserGroupService service = new(runner);

        UserGroupActionResult result = await service.SetPasswordAsync("ada", "unsafe\npassword");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task AccountNamesArePassedAsSeparateArgumentsAndValidated()
    {
        FakeProcessRunner runner = new FakeProcessRunner().Answer("groupadd", string.Empty);
        LinuxUserGroupService service = new(runner);

        UserGroupActionResult result = await service.CreateGroupAsync("-dangerous");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task FailedPasswordCreationRemovesTheNewUser()
    {
        FakeProcessRunner runner = new FakeProcessRunner()
            .Answer("useradd ", string.Empty)
            .Answer("chpasswd", string.Empty, exitCode: 1, stderr: "permission denied")
            .Answer("userdel ", string.Empty);
        LinuxUserGroupService service = new(runner);

        UserGroupActionResult result = await service.CreateUserAsync("ada", "Ada", "secret");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("userdel", runner.Calls.Last().FileName);
        Assert.IsFalse(result.Message.Contains("secret", StringComparison.Ordinal));
    }
}
