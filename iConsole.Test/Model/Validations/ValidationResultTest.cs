using iConsole.Model.Validations;

namespace iConsole.Test.Model.Validations;

[TestClass]
public class ValidationResultTest
{
    [TestMethod]
    public void ValidationResultConstructor()
    {
        var now = DateTime.Now;
        var result = new ValidationResult(now, true, "Valid");
        Assert.AreEqual(now, result.TimeStamp);
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("Valid", result.ErrorMessage);
    }

    [TestMethod]
    public void ValidationResultInvalid()
    {
        var now = DateTime.Now;
        var result = new ValidationResult(now, false, "Error message");
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Error message", result.ErrorMessage);
    }

    [TestMethod]
    public void ValidationResultNullErrorMessage()
    {
        var now = DateTime.Now;
        var result = new ValidationResult(now, true, null);
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void ValidationResultTimeStamp()
    {
        var now = DateTime.Now;
        var result = new ValidationResult(now, true, string.Empty);
        Assert.AreEqual(now, result.TimeStamp);
    }

    [TestMethod]
    public void ValidationResultProperties()
    {
        var now = DateTime.Now;
        var result = new ValidationResult(now, false, "Test Error");
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Test Error", result.ErrorMessage);
        Assert.IsTrue(result.TimeStamp <= DateTime.Now);
    }
}
