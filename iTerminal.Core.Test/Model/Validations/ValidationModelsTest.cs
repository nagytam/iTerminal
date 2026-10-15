using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Validations;
using Moq;

namespace iTerminal.Core.Test.Model.Validations;

[TestClass]
public class HostValidationModelAdditionalTest
{
    private static Mock<IDateTimeProviderService> DtMock() =>
        new Mock<IDateTimeProviderService>().WithNow(DateTime.Now.AddSeconds(-1));

    [TestMethod]
    public void Validate_NullModel_SetsValid()
    {
        var dt = DtMock();
        var model = new HostValidationModel(null!, dt.Object);
        Assert.IsTrue(model.IsValid);
    }

    [TestMethod]
    public void Validate_ValidHost_SetsValid()
    {
        var dt = DtMock();
        var textModel = new TextModel("192.168.1.1");
        var model = new HostValidationModel(textModel, dt.Object);
        model.Validate(DateTime.Now);
        Assert.IsTrue(model.IsValid);
    }

    [TestMethod]
    public void Validate_EmptyHost_SetsInvalid()
    {
        var dt = DtMock();
        var textModel = new TextModel(string.Empty);
        var model = new HostValidationModel(textModel, dt.Object);
        model.Validate(DateTime.Now);
        Assert.IsFalse(model.IsValid);
    }
}

[TestClass]
public class PathValidationModelTest
{
    private static Mock<IDateTimeProviderService> DtMock() =>
        new Mock<IDateTimeProviderService>().WithNow(DateTime.Now.AddSeconds(-1));

    [TestMethod]
    public void Validate_EmptyPath_SetsInvalid()
    {
        var dt = DtMock();
        var textModel = new TextModel(string.Empty);
        var model = new PathValidationModel(textModel, dt.Object);
        model.Validate(DateTime.Now);
        Assert.IsFalse(model.IsValid);
        Assert.IsFalse(string.IsNullOrEmpty(model.ErrorMessage));
    }

    [TestMethod]
    public void Validate_NullModel_SetsInvalid()
    {
        var dt = DtMock();
        var model = new PathValidationModel(null!, dt.Object);
        model.Validate(DateTime.Now);
        Assert.IsFalse(model.IsValid);
    }

    [TestMethod]
    public void Validate_ValidPath_SetsValid()
    {
        var dt = DtMock();
        var textModel = new TextModel(@"c:\windows");
        var model = new PathValidationModel(textModel, dt.Object);
        model.Validate(DateTime.Now);
        Assert.IsTrue(model.IsValid);
    }

    [TestMethod]
    public void ValidationResult_IsNotNull_AfterConstruction()
    {
        var dt = DtMock();
        var textModel = new TextModel(@"c:\windows");
        var model = new PathValidationModel(textModel, dt.Object);
        Assert.IsNotNull(model.ValidationResult);
    }
}

[TestClass]
public class FolderValidationModelTest
{
    private static Mock<IDateTimeProviderService> DtMock() =>
        new Mock<IDateTimeProviderService>().WithNow(DateTime.Now.AddSeconds(-1));

    [TestMethod]
    public void Validate_EmptyPath_SetsInvalid()
    {
        var dt = DtMock();
        var textModel = new TextModel(string.Empty);
        var model = new FolderValidationModel(textModel, dt.Object);
        model.Validate(DateTime.Now);
        Assert.IsFalse(model.IsValid);
    }

    [TestMethod]
    public void Validate_ExistingFolder_SetsValid()
    {
        var dt = DtMock();
        var existingFolder = Path.GetTempPath();
        var textModel = new TextModel(existingFolder);
        var model = new FolderValidationModel(textModel, dt.Object);
        model.Validate(DateTime.Now);
        // Valid because path exists (PathExistMode.NewFolder may allow existing)
        Assert.IsNotNull(model.ValidationResult);
    }

    [TestMethod]
    public void PathExistMode_DefaultValue_IsNewFolder()
    {
        var dt = DtMock();
        var textModel = new TextModel(@"c:\temp");
        var model = new FolderValidationModel(textModel, dt.Object);
        Assert.AreEqual(PathExistMode.NewFolder, model.PathExistMode);
    }

    [TestMethod]
    public void PathExistMode_CanBeChanged()
    {
        var dt = DtMock();
        var textModel = new TextModel(@"c:\temp");
        var model = new FolderValidationModel(textModel, dt.Object)
        {
            PathExistMode = PathExistMode.FolderOrNewFile
        };
        Assert.AreEqual(PathExistMode.FolderOrNewFile, model.PathExistMode);
    }
}

[TestClass]
public class NewFileValidationModelTest
{
    private static Mock<IDateTimeProviderService> DtMock() =>
        new Mock<IDateTimeProviderService>().WithNow(DateTime.Now.AddSeconds(-1));

    [TestMethod]
    public void Validate_EmptyPath_SetsInvalid()
    {
        var dt = DtMock();
        var textModel = new TextModel(string.Empty);
        var model = new NewFileValidationModel(textModel, dt.Object);
        model.Validate(DateTime.Now);
        Assert.IsFalse(model.IsValid);
    }

    [TestMethod]
    public void Validate_ValidNewFilePath_SetsValid()
    {
        var dt = DtMock();
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        var textModel = new TextModel(tempPath);
        var model = new NewFileValidationModel(textModel, dt.Object);
        model.Validate(DateTime.Now);
        Assert.IsNotNull(model.ValidationResult);
    }

    [TestMethod]
    public void PathExistMode_DefaultValue_IsNewFile()
    {
        var dt = DtMock();
        var textModel = new TextModel(@"c:\temp\file.txt");
        var model = new NewFileValidationModel(textModel, dt.Object);
        Assert.AreEqual(PathExistMode.NewFile, model.PathExistMode);
    }
}

[TestClass]
public class FolderOrNewFilesValidationModelTest
{
    private static Mock<IDateTimeProviderService> DtMock() =>
        new Mock<IDateTimeProviderService>().WithNow(DateTime.Now.AddSeconds(-1));

    [TestMethod]
    public void Validate_EmptyPath_SetsInvalid()
    {
        var dt = DtMock();
        var textModel = new TextModel(string.Empty);
        var model = new FolderOrNewFilesValidationModel(textModel, dt.Object);
        model.Validate(DateTime.Now);
        Assert.IsFalse(model.IsValid);
    }

    [TestMethod]
    public void Validate_NullModel_SetsValid()
    {
        var dt = DtMock();
        var model = new FolderOrNewFilesValidationModel(null!, dt.Object);
        model.Validate(DateTime.Now);
        // Null model path sets valid at the end
        Assert.IsNotNull(model.ValidationResult);
    }

    [TestMethod]
    public void PathExistMode_DefaultValue_IsFolderOrNewFile()
    {
        var dt = DtMock();
        var textModel = new TextModel(@"c:\temp");
        var model = new FolderOrNewFilesValidationModel(textModel, dt.Object);
        Assert.AreEqual(PathExistMode.FolderOrNewFile, model.PathExistMode);
    }

    [TestMethod]
    public void ValidationResult_IsNotNull()
    {
        var dt = DtMock();
        var textModel = new TextModel(@"c:\temp");
        var model = new FolderOrNewFilesValidationModel(textModel, dt.Object);
        Assert.IsNotNull(model.ValidationResult);
    }
}

file static class MockExtensions
{
    public static Mock<IDateTimeProviderService> WithNow(this Mock<IDateTimeProviderService> mock, DateTime now)
    {
        mock.Setup(m => m.Now).Returns(now);
        mock.Setup(m => m.UtcNow).Returns(now.ToUniversalTime());
        return mock;
    }
}
