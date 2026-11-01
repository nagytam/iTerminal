using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Validations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Model.Validations.Test;

/// <summary>A copy target may be an existing folder (copy into it) or a new name; an existing file is refused, and says so.</summary>
[TestClass]
public class FolderOrNewFilesValidationModelTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "FolderOrNewFilesValidationModelTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_folder, "existing-folder"));
        File.WriteAllText(Path.Combine(_folder, "existing-file.txt"), "keep me");
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

    private static FolderOrNewFilesValidationModel Model(string input)
    {
        var model = new FolderOrNewFilesValidationModel(new TextModel(input), Mock.Of<IDateTimeProviderService>());
        model.Validate(DateTime.Now);
        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnExistingFolder_IsAcceptedAsTheTarget()
    {
        var model = Model(Path.Combine(_folder, "existing-folder"));

        Assert.IsTrue(model.IsValid, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnExistingFile_IsRefusedAndSaysItAlreadyExists()
    {
        var model = Model(Path.Combine(_folder, "existing-file.txt"));

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(UrlValidationModel.ExistsMessage, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANewName_IsAccepted()
    {
        var model = Model(Path.Combine(_folder, "fresh.txt"));

        Assert.IsTrue(model.IsValid, model.ErrorMessage);
    }
}
