// Step bindings for Features/Application/Application.ScreenManager.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Application;

[Binding]
public sealed class ScreenManagerSteps
{
    [Given(@"the screen manager is open")]
    public void TheScreenManagerIsOpen() => throw new PendingStepException();

    [Then(@"the screen on show is marked in the list")]
    public void TheScreenOnShowIsMarkedInTheList() => throw new PendingStepException();

    [Then(@"no row shows {string}")]
    public void NoRowShowsValue(string value1) => throw new PendingStepException();

    [Then(@"the screen manager is still open")]
    public void TheScreenManagerIsStillOpen() => throw new PendingStepException();

    [Then(@"the hint row says that screens cannot be switched")]
    public void TheHintRowSaysThatScreensCannotBeSwitched() => throw new PendingStepException();
}
