using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Model.Validations.Test;

/// <summary>The Create file name box: a name is checked in the panel's folder, the way the create command writes it.</summary>
[TestClass]
public class NewFileValidationModelTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "NewFileValidationModelTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        File.WriteAllText(Path.Combine(_folder, "taken.txt"), "keep me");
        File.WriteAllText(Path.Combine(_folder, "sub", "taken.txt"), "keep me too");
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

    private static NewFileValidationModel Model(string input, Url? baseUrl)
    {
        var dateTime = Mock.Of<IDateTimeProviderService>();
        var model = new NewFileValidationModel(new TextModel(input), dateTime) { BaseUrl = baseUrl };
        model.Validate(DateTime.Now);
        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EmptyName_IsInvalidButSaysNothing()
    {
        var model = Model(string.Empty, new Url(_folder));

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(string.Empty, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NameTakenInTheFolder_IsRefusedAndSaysWhy()
    {
        var model = Model("taken.txt", new Url(_folder));

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(UrlValidationModel.ExistsMessage, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NameWithAFolderTakenInThatFolder_IsRefused()
    {
        // The file sits in sub/, so the check must look there — not in the working directory, where it does not exist.
        var model = Model("sub/taken.txt", new Url(_folder));

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(UrlValidationModel.ExistsMessage, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NewNameInAnExistingSubfolder_IsAccepted()
    {
        var model = Model("sub/fresh.txt", new Url(_folder));

        Assert.IsTrue(model.IsValid, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RootedPath_IsCheckedAsTyped_NotInsideTheFolder()
    {
        var rooted = Path.Combine(_folder, "taken.txt");

        var model = Model(rooted, new Url(Path.GetTempPath()));

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(UrlValidationModel.ExistsMessage, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NameWithANulCharacter_IsRefusedAndSaysWhy_NotThrown()
    {
        // Pasted text can carry a NUL; the file system cannot represent it, and the key handler must not throw.
        var model = Model("bad\0name.txt", new Url(_folder));

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(UrlValidationModel.InvalidMessage, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAFolder_TheNameIsCheckedAsTyped()
    {
        var model = Model("taken.txt", null);

        Assert.IsTrue(model.IsValid, "the working directory does not hold taken.txt");
    }
}
