using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Validations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Model.Validations.Test;

/// <summary>The path box: an empty field is invalid without a message; a path the file system would refuse says so.</summary>
[TestClass]
public class PathValidationModelTest
{
    private static PathValidationModel Model(string input)
    {
        var model = new PathValidationModel(new TextModel(input), Mock.Of<IDateTimeProviderService>());
        model.Validate(DateTime.Now);
        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EmptyPath_IsInvalidButSaysNothing()
    {
        var model = Model(string.Empty);

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(string.Empty, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathWithANulCharacter_IsRefusedAndSaysWhy_NotThrown()
    {
        var model = Model("bad\0path");

        Assert.IsFalse(model.IsValid);
        Assert.AreEqual(UrlValidationModel.InvalidMessage, model.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NullModel_IsInvalid()
    {
        var model = new PathValidationModel(null!, Mock.Of<IDateTimeProviderService>());
        model.Validate(DateTime.Now);

        Assert.IsFalse(model.IsValid);
    }
}
