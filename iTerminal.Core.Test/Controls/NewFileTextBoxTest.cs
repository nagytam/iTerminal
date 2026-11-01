using iTerminal.Core.Model.Validations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Controls;

namespace iTerminal.Core.Controls.Test;

[TestClass]
public class NewFileTextBoxTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ANameThatIsTakenSaysSoRatherThanCallingThePathInvalid()
    {
        // The existence check was handed the whole "file://" URL, which never exists, so a taken
        // name was reported as "Path is not valid!".
        var path = Path.Combine(Path.GetTempPath(), "taken-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        try
        {
            var box = new NewFileTextBox(Moq.Mock.Of<iConsole.Services.IBeepService>(), new iConsole.Services.DateTimeProviderService(), new iConsole.Test.Core.TestThemeService())
            {
                Text = path,
            };

            box.ValidationModel!.Validate(DateTime.Now.AddSeconds(1));

            Assert.IsFalse(box.ValidationModel.IsValid);
            Assert.AreEqual(UrlValidationModel.ExistsMessage, box.ValidationModel.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
