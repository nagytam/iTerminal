using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Model.Validations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.Test;

/// <summary>The copy target box: its validation is the folder-or-new-file rule, so a folder is a valid target and a file is refused.</summary>
[TestClass]
public class FolderOrNewFileTextBoxTest
{
    [TestMethod]
    public void IsValidatedByTheFolderOrNewFileRule()
    {
        var box = new FolderOrNewFileTextBox(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        Assert.IsInstanceOfType<FolderOrNewFilesValidationModel>(box.ValidationModel);
    }
}
