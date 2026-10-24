using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Commands.Test;

/// <summary>
/// Enter in the Create folder dialog: the folder is made in the panel's folder and the dialog closes; a failure is
/// said on the dialog's error line and the dialog stays open with the name still in the box.
/// </summary>
[TestClass]
public class CreateFolderCommandTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "CreateFolderCommandTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
        }
    }

    private CreateFolderDialog NewDialog(IContentModelService service) =>
        new(service, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { BaseUrl = new Url(_folder) };

    private static IContentModelService ServiceReturning(IContentModel? model)
    {
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetLastContentModel()).Returns(model);
        return service.Object;
    }

    private static async Task<Control> TypeNameAndFocusAsync(CreateFolderDialog dialog, string name)
    {
        var root = await ShowAsync(dialog);
        dialog.NameBoxForTest.Text = name;
        dialog.NameBoxForTest.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));
        dialog.NameBoxForTest.SetFocus();
        return root;
    }

    [TestMethod]
    public async Task Enter_CreatesTheFolderInThePanelsFolderAndClosesTheDialog()
    {
        var model = new Mock<IContentModel>();
        var dialog = NewDialog(ServiceReturning(model.Object));
        var root = await TypeNameAndFocusAsync(dialog, "projects");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        model.Verify(m => m.CreateFolderAsync(It.Is<Url>(url => url.FullName.StartsWith(Path.Combine(_folder, "projects")))), Times.Once);
        model.Verify(m => m.Update(), Times.Once);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog), "a created folder closes the dialog");
    }

    [TestMethod]
    public async Task Enter_WhenTheCreateFails_KeepsTheDialogOpenAndSaysWhy()
    {
        var model = new Mock<IContentModel>();
        model.Setup(m => m.CreateFolderAsync(It.IsAny<Url>())).ThrowsAsync(new IOException("Access denied"));
        var dialog = NewDialog(ServiceReturning(model.Object));
        var root = await TypeNameAndFocusAsync(dialog, "projects");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "a failed create leaves the dialog open");
        Assert.AreEqual("projects", dialog.NewFolderName, "the name stays in the box so it can be changed");
        AssertScreenContains(Render(dialog), "Could not create the folder: Access denied");
    }

    [TestMethod]
    public async Task Enter_WithNoFolderToCreateIn_SaysSo()
    {
        var dialog = NewDialog(ServiceReturning(null));
        await TypeNameAndFocusAsync(dialog, "projects");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        AssertScreenContains(Render(dialog), "There is no folder to create the folder in.");
    }
}
