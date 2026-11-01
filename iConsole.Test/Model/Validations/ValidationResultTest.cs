using iConsole.Model.Validations;

namespace iConsole.Test.Model.Validations;

[TestClass]
public class ValidationResultTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ValidationResultConstructor()
    {
        var now = DateTime.Now;
        var result = new ValidationResult(now, true, "Valid");
        Assert.AreEqual(now, result.TimeStamp);
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("Valid", result.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidationResultInvalid()
    {
        var now = DateTime.Now;
        var result = new ValidationResult(now, false, "Error message");
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Error message", result.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidationResultNullErrorMessage()
    {
        var now = DateTime.Now;
        var result = new ValidationResult(now, true, null);
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidationResultTimeStamp()
    {
        var now = DateTime.Now;
        var result = new ValidationResult(now, true, string.Empty);
        Assert.AreEqual(now, result.TimeStamp);
    }

    [Timeout(60_000)]
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
