using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Model.Validations;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.Test;

/// <summary>The Create-folder name box: it must name a folder that does not exist yet.</summary>
[TestClass]
public class FolderTextBoxTest
{
    private static FolderTextBox Create(string text) =>
        new(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { Text = text };

    [Timeout(60_000)]
    [TestMethod]
    public void ValidatesAsANewFolder()
    {
        Assert.IsInstanceOfType<FolderValidationModel>(Create(string.Empty).ValidationModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyBoxIsInvalidButSaysNothing()
    {
        var box = Create(string.Empty);

        box.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));

        Assert.IsFalse(box.ValidationModel.IsValid);
        Assert.AreEqual(string.Empty, box.ValidationModel.ErrorMessage, "an untouched field is not an error; the button stays disabled instead");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFolderThatAlreadyExistsIsRejectedAndSaysWhy()
    {
        var box = Create(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));

        box.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));

        Assert.IsFalse(box.ValidationModel.IsValid);
        Assert.AreEqual(UrlValidationModel.ExistsMessage, box.ValidationModel.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANewFolderUnderAnExistingOneIsAccepted()
    {
        var box = Create(Path.Combine(Path.GetTempPath(), "new-" + Guid.NewGuid().ToString("N")));

        box.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));

        Assert.IsTrue(box.ValidationModel.IsValid, box.ValidationModel.ErrorMessage);
    }
}
