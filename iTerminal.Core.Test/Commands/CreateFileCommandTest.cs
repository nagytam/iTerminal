using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Validations;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Commands.Test;

/// <summary>
/// Enter in the Create file dialog: the file lands in the panel's folder, the dialog closes on success, and a
/// failure is said on the dialog's error line while the dialog stays open with the name still in the box.
/// </summary>
[TestClass]
public class CreateFileCommandTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "CreateFileCommandTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "taken.txt"), "keep me");
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

    private CreateFileDialog NewDialog(IContentModelService service) =>
        new(service, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { BaseUrl = new Url(_folder) };

    private static IContentModelService ServiceReturning(IContentModel? model)
    {
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetLastContentModel()).Returns(model);
        return service.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_CreatesTheFileInThePanelsFolderAndClosesTheDialog()
    {
        var model = new Mock<IContentModel>();
        var dialog = NewDialog(ServiceReturning(model.Object));
        var root = await ShowAsync(dialog);
        dialog.NameBoxForTest.Text = "new.txt";
        dialog.NameBoxForTest.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));
        dialog.NameBoxForTest.SetFocus();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        model.Verify(m => m.WriteFileAsync(
            It.Is<Url>(url => url.FullName == Path.Combine(_folder, "new.txt")),
            It.IsAny<MemoryStream>()), Times.Once);
        model.Verify(m => m.Update(), Times.Once);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog), "a created file closes the dialog");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_WhenTheWriteFails_KeepsTheDialogOpenAndSaysWhy()
    {
        var model = new Mock<IContentModel>();
        model.Setup(m => m.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>())).ThrowsAsync(new IOException("Access denied"));
        var dialog = NewDialog(ServiceReturning(model.Object));
        var root = await ShowAsync(dialog);
        dialog.NameBoxForTest.Text = "new.txt";
        dialog.NameBoxForTest.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));
        dialog.NameBoxForTest.SetFocus();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "a failed create leaves the dialog open");
        Assert.AreEqual("new.txt", dialog.NewFileName, "the name stays in the box so it can be changed");
        AssertScreenContains(Render(dialog), "Could not create the file: Access denied");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingAfterAFailure_WithdrawsTheMessage()
    {
        var model = new Mock<IContentModel>();
        model.Setup(m => m.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>())).ThrowsAsync(new IOException("Access denied"));
        var dialog = NewDialog(ServiceReturning(model.Object));
        await ShowAsync(dialog);
        dialog.NameBoxForTest.Text = "new.txt";
        dialog.NameBoxForTest.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));
        dialog.NameBoxForTest.SetFocus();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        await dialog.HandleKeyEventAsync(Char('2'));

        AssertScreenDoesNotContain(Render(dialog), "Could not create the file");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_WithNoFolderToCreateIn_SaysSo()
    {
        var dialog = NewDialog(ServiceReturning(null));
        await ShowAsync(dialog);
        dialog.NameBoxForTest.Text = "new.txt";
        dialog.NameBoxForTest.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));
        dialog.NameBoxForTest.SetFocus();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        AssertScreenContains(Render(dialog), "There is no folder to create the file in.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_OverAnExistingFile_RefusesAndDoesNotTruncateIt()
    {
        // Validation normally stops this before the command runs; the command must still refuse on its own.
        var model = new Mock<IContentModel>();
        var dialog = NewDialog(ServiceReturning(model.Object));
        await ShowAsync(dialog);
        var command = new CreateFileCommand((NewFileValidationModel)dialog.NameBoxForTest.ValidationModel!, ServiceReturning(model.Object));
        dialog.NameBoxForTest.Text = "taken.txt";
        var button = new Button("Create file", command, TestTheme());
        dialog.AddControl(button);

        await command.ExecuteAsync(null);

        model.Verify(m => m.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()), Times.Never);
        AssertScreenContains(Render(dialog), UrlValidationModel.ExistsMessage);
        Assert.AreEqual("keep me", File.ReadAllText(Path.Combine(_folder, "taken.txt")));
    }
}
