using iTerminal.Core.Model.Validations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Shift+F7: create a folder in the focused panel's folder.</summary>
[TestClass]
public class CreateFolderDialogTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "CreateFolderDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_folder, "existing"));
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

    private CreateFolderDialog Create() =>
        new(Mock.Of<IContentModelService>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme())
        {
            BaseUrl = new Url(_folder),
        };

    private static (bool IsValid, string Message) Validate(CreateFolderDialog dialog, string name)
    {
        dialog.NameBoxForTest.Text = name;
        var validation = dialog.NameBoxForTest.ValidationModel!;
        validation.Validate(DateTime.Now.AddSeconds(1));
        return (validation.IsValid, validation.ErrorMessage);
    }

    [TestMethod]
    public void AFolderThatAlreadyExistsInThePanelIsReportedAsSuch()
    {
        var (isValid, message) = Validate(Create(), "existing");

        Assert.IsFalse(isValid);
        Assert.AreEqual(UrlValidationModel.ExistsMessage, message);
    }

    [TestMethod]
    public void ANewFolderNameIsAccepted()
    {
        var (isValid, _) = Validate(Create(), "brand-new");

        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public async Task OpensTitledForWhatItDoes()
    {
        var dialog = Create();
        await ShowAsync(dialog);

        Assert.AreEqual("Create folder", dialog.Title);
        AssertScreenContains(Render(dialog), "Name");
    }

    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var dialog = Create();
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}
