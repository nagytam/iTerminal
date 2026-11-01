using iConsole.Model.Validations;
using iConsole.Services;
using Moq;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iConsole.Test.Model.Validations;

[TestClass]
public class ValidationModelTest
{
    private class TestNotifiable : INotifyPropertyChanged
    {
        private string _value = "test";

        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidationModelConstructor()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var testModel = new TestNotifiable();
        var validationModel = new ValidationModel<TestNotifiable>(mockDateTimeProvider.Object, testModel);

        Assert.IsNotNull(validationModel);
        Assert.IsTrue(validationModel.IsValid);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidationModelIsValid()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var testModel = new TestNotifiable();
        var validationModel = new ValidationModel<TestNotifiable>(mockDateTimeProvider.Object, testModel);

        Assert.IsTrue(validationModel.IsValid);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidationModelValidationResult()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var testModel = new TestNotifiable();
        var validationModel = new ValidationModel<TestNotifiable>(mockDateTimeProvider.Object, testModel);

        Assert.IsNotNull(validationModel.ValidationResult);
        Assert.IsTrue(validationModel.ValidationResult.IsValid);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidationModelErrorMessage()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var testModel = new TestNotifiable();
        var validationModel = new ValidationModel<TestNotifiable>(mockDateTimeProvider.Object, testModel);

        Assert.IsNotNull(validationModel.ErrorMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidationModelWithNullModel()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var validationModel = new ValidationModel<TestNotifiable>(mockDateTimeProvider.Object, null!);

        Assert.IsNotNull(validationModel);
    }

    /// <summary>Valid when the value is not empty — the shape of every "type a name" field.</summary>
    private sealed class NonEmptyValidationModel(IDateTimeProviderService dateTimeProviderService, TestNotifiable model)
        : ValidationModel<TestNotifiable>(dateTimeProviderService, model)
    {
        public override void Validate(DateTime now)
        {
            if (Model is not null)
            {
                SetValidationResult(now, Model.Value.Length > 0, Model.Value.Length > 0 ? string.Empty : "empty");
            }
        }
    }

    /// <summary>
    /// The dialog validates with the real clock when it opens; the keystrokes that follow used to be
    /// stamped with a clock cached half a second behind it, so their results were thrown away and a
    /// name typed and confirmed quickly stayed "invalid" for good.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void AChangeAfterAValidationOnTheRealClockIsNotDiscarded()
    {
        var cachedClock = new Mock<IDateTimeProviderService>();
        cachedClock.Setup(m => m.Now).Returns(DateTime.Now.AddMilliseconds(-400));
        var model = new TestNotifiable { Value = string.Empty };
        var validator = new NonEmptyValidationModel(cachedClock.Object, model);
        validator.Validate(DateTime.Now);
        Assert.IsFalse(validator.IsValid);

        model.Value = "gamma";

        Assert.IsTrue(validator.EnsureCurrent(), "the typed name is what is validated");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EnsureCurrentSeesAChangeTheBackgroundHasNotValidatedYet()
    {
        var clock = new Mock<IDateTimeProviderService>();
        clock.Setup(m => m.Now).Returns(new DateTime(2026, 1, 1));
        var model = new TestNotifiable { Value = string.Empty };
        var validator = new NonEmptyValidationModel(clock.Object, model);

        model.Value = "x";

        Assert.IsTrue(validator.EnsureCurrent());
        Assert.IsFalse(validator.IsPending);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheNewestChangeWinsEvenWhenTheClockDoesNotMove()
    {
        var frozenClock = new Mock<IDateTimeProviderService>();
        frozenClock.Setup(m => m.Now).Returns(new DateTime(2026, 1, 1));
        var model = new TestNotifiable { Value = string.Empty };
        var validator = new NonEmptyValidationModel(frozenClock.Object, model);

        for (var i = 0; i < 50; i++)
        {
            model.Value = i % 2 == 0 ? "a" + i : string.Empty;
        }
        model.Value = "final";

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (validator.IsPending && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        Assert.IsTrue(validator.IsValid, "the last value is valid, whatever order the background validations finished in");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingPendingMeansNoWork()
    {
        var clock = new Mock<IDateTimeProviderService>();
        clock.Setup(m => m.Now).Returns(DateTime.Now);
        var validator = new NonEmptyValidationModel(clock.Object, new TestNotifiable());

        Assert.IsFalse(validator.IsPending);
        Assert.IsTrue(validator.EnsureCurrent());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidationModelInterface()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var testModel = new TestNotifiable();
        var validationModel = new ValidationModel<TestNotifiable>(mockDateTimeProvider.Object, testModel);

        Assert.IsInstanceOfType(validationModel, typeof(IValidationModel));
    }
}
