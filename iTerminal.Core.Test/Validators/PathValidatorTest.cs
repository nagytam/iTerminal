using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Validations;
using Moq;

namespace iTerminal.Core.Test.Validators;

[TestClass]
public class PathValidatorTest
{
    [TestMethod]
    public void IsValidWithNullValueReturnsFalse()
    {
        var mockDateTimeProviderService = new Mock<DateTimeProviderService>();
        var validator = new PathValidationModel(null!, mockDateTimeProviderService.Object);
        Assert.IsFalse(validator.IsValid);
    }

    [TestMethod]
    public void IsValidWithEmptyValueReturnsFalse()
    {
        var mockDateTimeProviderService = new Mock<DateTimeProviderService>();
        var textModel = new TextModel { Lines = [ string.Empty ] };
        var validator = new PathValidationModel(textModel, mockDateTimeProviderService.Object);
        Assert.IsFalse(validator.IsValid);
    }

    [TestMethod]
    public void IsValidWithValidFileNameReturnsFalse()
    {
        var mockDateTimeProviderService = new Mock<DateTimeProviderService>();
        var validator = new PathValidationModel(new TextModel("a"), mockDateTimeProviderService.Object);
        Assert.IsTrue(validator.IsValid);
        validator = new PathValidationModel(new TextModel("a."), mockDateTimeProviderService.Object);
        Assert.IsTrue(validator.IsValid);
        validator = new PathValidationModel(new TextModel("a.b"), mockDateTimeProviderService.Object);
        Assert.IsTrue(validator.IsValid);
        validator = new PathValidationModel(new TextModel("a.b."), mockDateTimeProviderService.Object);
        Assert.IsTrue(validator.IsValid);
        validator = new PathValidationModel(new TextModel("readme.txt"), mockDateTimeProviderService.Object);
        Assert.IsTrue(validator.IsValid);
    }
}
