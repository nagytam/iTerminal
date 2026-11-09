using iConsole.Prompts;
using iConsole.Services;
using iTerminal.Core.Dialogs.UsersAndGroups;
using iTerminal.Core.Services.UsersAndGroups;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.UsersAndGroups;

[TestClass]
public class UserGroupManagerDialogTest
{
    [TestMethod]
    public async Task LoadsAndFiltersUsersAndSwitchesToGroups()
    {
        Mock<IUserGroupService> service = CreateService();
        Mock<IPromptService> prompts = new(MockBehavior.Strict);
        UserGroupManagerDialog dialog = new(service.Object, prompts.Object, TestTheme());
        await ShowAsync(dialog);
        await WaitUntilAsync(() =>
        {
            dialog.ApplyPendingForTest();
            return !dialog.IsBusyForTest;
        }, "the local accounts to load");

        Assert.AreEqual(2, dialog.VisibleUsersForTest.Count);
        AssertScreenContains(Render(dialog), "ada");

        await dialog.HandleKeyEventAsync(Char('/'));
        await dialog.HandleKeyEventAsync(Char('b'));
        Assert.AreEqual("bob", dialog.VisibleUsersForTest.Single().Name);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        Assert.IsTrue(dialog.IsShowingGroupsForTest);
        Assert.AreEqual("staff", dialog.VisibleGroupsForTest.Single().Name);
        AssertScreenContains(Render(dialog), "staff");
    }

    [TestMethod]
    public async Task CreatingAUserUsesMaskedPasswordPromptAndRefreshesTheList()
    {
        List<LocalUser> users = [new("ada", "Ada Lovelace")];
        Mock<IUserGroupService> service = CreateService(users);
        service.Setup(item => item.CreateUserAsync("new-user", "New User", "hidden-password", It.IsAny<CancellationToken>()))
            .Callback(() => users.Add(new LocalUser("new-user", "New User")))
            .ReturnsAsync(new UserGroupActionResult(true, "new-user was created."));

        Queue<string?> answers = new(["new-user", "New User", "hidden-password"]);
        List<bool> secretPrompts = [];
        Mock<IPromptService> prompts = new();
        prompts.Setup(item => item.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>()))
            .Callback<TextPrompt<string>, CancellationToken>((prompt, _) => secretPrompts.Add(prompt.IsSecret))
            .Returns(() => Task.FromResult(answers.Dequeue()));

        UserGroupManagerDialog dialog = new(service.Object, prompts.Object, TestTheme());
        await ShowAsync(dialog);
        await WaitUntilAsync(() =>
        {
            dialog.ApplyPendingForTest();
            return !dialog.IsBusyForTest;
        }, "the initial accounts to load");
        await dialog.HandleKeyEventAsync(Char('n'));
        await WaitUntilAsync(() =>
        {
            dialog.ApplyPendingForTest();
            return !dialog.IsBusyForTest && dialog.StatusForTest.Contains("created", StringComparison.OrdinalIgnoreCase);
        },
            "the user creation to finish");

        Assert.AreEqual(3, secretPrompts.Count);
        Assert.IsTrue(secretPrompts[2]);
        Assert.IsTrue(dialog.VisibleUsersForTest.Any(user => user.Name == "new-user"));
        AssertScreenDoesNotContain(Render(dialog), "hidden-password");
        service.Verify(item => item.CreateUserAsync("new-user", "New User", "hidden-password", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeletingAUserRequiresConfirmation()
    {
        Mock<IUserGroupService> service = CreateService();
        Mock<IPromptService> prompts = new();
        prompts.SetupSequence(item => item.ConfirmAsync(It.IsAny<ConfirmationPrompt>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        UserGroupManagerDialog dialog = new(service.Object, prompts.Object, TestTheme());
        await ShowAsync(dialog);
        await WaitUntilAsync(() =>
        {
            dialog.ApplyPendingForTest();
            return !dialog.IsBusyForTest;
        }, "the initial accounts to load");

        await dialog.HandleKeyEventAsync(Char('d'));
        service.Verify(item => item.DeleteUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        await dialog.HandleKeyEventAsync(Char('d'));
        await WaitUntilAsync(() =>
        {
            dialog.ApplyPendingForTest();
            return !dialog.IsBusyForTest && dialog.StatusForTest.Contains("deleted", StringComparison.OrdinalIgnoreCase);
        },
            "the confirmed deletion to finish");

        service.Verify(item => item.DeleteUserAsync("ada", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SelectionScrollsToKeepSelectedAccountVisible()
    {
        List<LocalUser> users = Enumerable.Range(0, 30)
            .Select(index => new LocalUser($"user-{index:D2}", $"User {index:D2}"))
            .ToList();
        Mock<IUserGroupService> service = CreateService(users);
        Mock<IPromptService> prompts = new(MockBehavior.Strict);
        UserGroupManagerDialog dialog = new(service.Object, prompts.Object, TestTheme());
        await ShowAsync(dialog);
        await WaitUntilAsync(() =>
        {
            dialog.ApplyPendingForTest();
            return !dialog.IsBusyForTest;
        }, "the local accounts to load");

        for (int index = 0; index < 20; index++)
        {
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow, '\0'));
        }

        AssertScreenContains(Render(dialog), "user-20");
    }

    private static Mock<IUserGroupService> CreateService(List<LocalUser>? users = null)
    {
        users ??= [new LocalUser("ada", "Ada Lovelace"), new LocalUser("bob", "Bob Example")];
        Mock<IUserGroupService> service = new();
        service.Setup(item => item.ListUsersAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<IReadOnlyList<LocalUser>>([.. users]));
        service.Setup(item => item.ListGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LocalGroup("staff")]);
        service.Setup(item => item.DeleteUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => new UserGroupActionResult(true, $"{name} was deleted."));
        return service;
    }
}
