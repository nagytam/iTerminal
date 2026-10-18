using iConsole.Controls;
using iConsole.Controls.Wizard;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class WizardTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static Wizard Create(Func<Task<string?>>? validateFirst = null)
    {
        IThemeService theme = Theme();
        Wizard wizard = new(theme) { ViewSize = new Size(40, 8) };
        wizard.AddStep(new WizardStep("Name", new Label("page", theme), validateFirst));
        wizard.AddStep(new WizardStep("Options", new Label("page", theme)));
        wizard.AddStep(new WizardStep("Done", new Label("page", theme)));
        return wizard;
    }

    [TestMethod]
    public async Task Next_WalksForwardAndBack_ShowingOnlyCurrentPage()
    {
        Wizard wizard = Create();
        Assert.IsTrue(wizard.Steps[0].Content.IsVisible);
        Assert.IsTrue(await wizard.NextAsync());
        Assert.AreEqual(1, wizard.CurrentIndex);
        Assert.IsFalse(wizard.Steps[0].Content.IsVisible);
        Assert.IsTrue(wizard.Steps[1].Content.IsVisible);
        Assert.IsTrue(wizard.Back());
        Assert.AreEqual(0, wizard.CurrentIndex);
        Assert.IsFalse(wizard.Back());
    }

    [TestMethod]
    public async Task Next_StaysPutAndShowsError_WhenValidationFails()
    {
        Wizard wizard = Create(() => Task.FromResult<string?>("Name is required"));
        Assert.IsFalse(await wizard.NextAsync());
        Assert.AreEqual(0, wizard.CurrentIndex);
        Assert.AreEqual("Name is required", wizard.Error);

        MockBuffer buffer = new(new Size(40, 8));
        wizard.Draw(buffer);
        foreach (IControl child in wizard.Controls)
        {
            child.Draw(buffer);
        }
        Assert.IsTrue(buffer.ContentAsString.Contains("Step 1 of 3: Name"));
        Assert.IsTrue(buffer.ContentAsString.Contains("Name is required"));
    }

    [TestMethod]
    public async Task Next_OnLastStep_RaisesFinishedOnce()
    {
        Wizard wizard = Create();
        int finished = 0;
        wizard.Finished += (_, _) => finished++;
        await wizard.NextAsync();
        await wizard.NextAsync();
        Assert.IsTrue(wizard.IsLastStep);
        Assert.IsTrue(await wizard.NextAsync());
        Assert.IsFalse(await wizard.NextAsync());
        Assert.IsTrue(wizard.IsFinished);
        Assert.AreEqual(1, finished);
    }

    [TestMethod]
    public async Task ShowAsync_KeepsOnlyCurrentPageVisible()
    {
        Wizard wizard = Create();
        await wizard.ShowAsync();
        Assert.IsTrue(wizard.Steps[0].Content.IsVisible);
        Assert.IsFalse(wizard.Steps[1].Content.IsVisible);
        Assert.IsFalse(wizard.Steps[2].Content.IsVisible);
    }
}
