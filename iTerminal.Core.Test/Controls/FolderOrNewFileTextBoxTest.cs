using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Model.Validations;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.Test;

/// <summary>
/// The Copy/Move target box: an existing folder to copy into, or a file name that is not taken yet.
/// </summary>
[TestClass]
public class FolderOrNewFileTextBoxTest
{
    private static FolderOrNewFileTextBox Create(string text) =>
        new(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { Text = text };

    private static bool IsValid(string text)
    {
        var box = Create(text);
        box.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));
        return box.ValidationModel.IsValid;
    }

    [TestMethod]
    public void ValidatesAsAFolderOrANewFile()
    {
        Assert.IsInstanceOfType<FolderOrNewFilesValidationModel>(Create(string.Empty).ValidationModel);
    }

    [TestMethod]
    public void AnExistingFolderIsAValidTarget()
    {
        Assert.IsTrue(IsValid(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
    }

    [TestMethod]
    public void ANewFileNameIsAValidTarget()
    {
        Assert.IsTrue(IsValid(Path.Combine(Path.GetTempPath(), "new-" + Guid.NewGuid().ToString("N") + ".txt")));
    }

    [TestMethod]
    public void AnExistingFileIsNotATarget()
    {
        var path = Path.Combine(Path.GetTempPath(), "taken-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        try
        {
            Assert.IsFalse(IsValid(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnEmptyBoxIsInvalidButSaysNothing()
    {
        var box = Create(string.Empty);

        box.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));

        Assert.IsFalse(box.ValidationModel.IsValid);
        Assert.AreEqual(string.Empty, box.ValidationModel.ErrorMessage, "an untouched field is not an error; the button stays disabled instead");
    }
}
