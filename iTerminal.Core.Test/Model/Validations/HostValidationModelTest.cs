using iConsole.Controls;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Validations;
using Moq;

namespace iTerminal.Core.Test.Model.Validations;

[TestClass]
public class HostValidationModelTest
{
    [TestMethod]
    public void EmptyHost()
    {
        var mockDateTimeProviderService = new Mock<IDateTimeProviderService>();
        var validationModel = new HostValidationModel(new TextModel(string.Empty), mockDateTimeProviderService.Object);
        Assert.IsFalse(validationModel.IsValid);
    }

    [TestMethod]
    public void HostOnly()
    {
        var mockDateTimeProviderService = new Mock<IDateTimeProviderService>();
        var validationModel = new HostValidationModel(new TextModel("ssh://nagy.ovh"), mockDateTimeProviderService.Object);
        Assert.IsTrue(validationModel.IsValid);
    }

    [TestMethod]
    public void UserNameAndHostOnly()
    {
        var mockDateTimeProviderService = new Mock<IDateTimeProviderService>();
        var validationModel = new HostValidationModel(new TextModel("tamas@nagy.ovh"), mockDateTimeProviderService.Object);
        Assert.IsTrue(validationModel.IsValid);
    }
}
