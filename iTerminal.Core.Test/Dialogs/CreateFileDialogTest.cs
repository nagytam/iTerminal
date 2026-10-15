using iTerminal.Core.Model.Validations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Shift+F4: create an empty file in the focused panel's folder.</summary>
[TestClass]
public class CreateFileDialogTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "CreateFileDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private CreateFileDialog Create()
    {
        var dialog = new CreateFileDialog(Mock.Of<IContentModelService>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme())
        {
            BaseUrl = new Url(_folder),
        };
        return dialog;
    }

    private static (bool IsValid, string Message) Validate(CreateFileDialog dialog, string name)
    {
        dialog.NameBoxForTest.Text = name;
        var validation = dialog.NameBoxForTest.ValidationModel!;
        validation.Validate(DateTime.Now.AddSeconds(1));
        return (validation.IsValid, validation.ErrorMessage);
    }

    [TestMethod]
    public void ANameAlreadyTakenInThePanelsFolderIsRefusedAndSaysWhy()
    {
        // The bare name was checked against the process's working directory, so a file sitting in
        // the panel was not "taken" — and creating it replaced its contents with nothing.
        var (isValid, message) = Validate(Create(), "taken.txt");

        Assert.IsFalse(isValid);
        Assert.AreEqual(UrlValidationModel.ExistsMessage, message);
    }

    [TestMethod]
    public void AFreshNameIsAccepted()
    {
        var (isValid, _) = Validate(Create(), "fresh.txt");

        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void AnEmptyNameIsInvalidButSaysNothing()
    {
        var (isValid, message) = Validate(Create(), string.Empty);

        Assert.IsFalse(isValid);
        Assert.AreEqual(string.Empty, message, "no red text before anything is typed");
    }

    [TestMethod]
    public async Task OpensWithAnEmptyNameBoxAndItsButtons()
    {
        var dialog = Create();
        await ShowAsync(dialog);

        var screen = Render(dialog);

        Assert.AreEqual("Create file", dialog.Title);
        AssertScreenContains(screen, "Name");
        AssertScreenContains(screen, "Cancel", "the Cancel button reads Cancel, not a cut-off command name");
    }

    [TestMethod]
    public async Task TheCreateCommandWillNotRunOverATakenName()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        Validate(dialog, "taken.txt");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual("keep me", File.ReadAllText(Path.Combine(_folder, "taken.txt")));
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
