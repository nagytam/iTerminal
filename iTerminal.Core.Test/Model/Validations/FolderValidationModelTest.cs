using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Model.Validations.Test;

/// <summary>The Create folder name box: a name is checked in the panel's folder, the way the create command makes it.</summary>
[TestClass]
public class FolderValidationModelTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "FolderValidationModelTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_folder, "sub", "existing"));
        Directory.CreateDirectory(Path.Combine(_folder, "taken"));
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

    private static FolderValidationModel Model(string input, Url? baseUrl)
    {
        var model = new FolderValidationModel(new TextModel(input), Mock.Of<IDateTimeProviderService>()) { BaseUrl = baseUrl };
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
        var model = Model("taken", new Url(_folder));

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(UrlValidationModel.ExistsMessage, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NameWithAFolderTakenInThatFolder_IsRefused()
    {
        var model = Model("sub/existing", new Url(_folder));

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(UrlValidationModel.ExistsMessage, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NewNameInAnExistingSubfolder_IsAccepted()
    {
        var model = Model("sub/fresh", new Url(_folder));

        Assert.IsTrue(model.IsValid, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAFolder_TheNameIsCheckedAsTyped()
    {
        var model = Model("taken", null);

        Assert.IsTrue(model.IsValid, "the working directory does not hold a folder called taken");
    }
}
