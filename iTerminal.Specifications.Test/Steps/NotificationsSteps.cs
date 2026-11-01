// Step bindings for Features/Notifications.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class NotificationsSteps
{
    [When(@"a copy finishes")]
    public void WhenACopyFinishes() => throw new PendingStepException();

    [Then(@"a toast says the copy is complete")]
    public void ThenAToastSaysTheCopyIsComplete() => throw new PendingStepException();

    [Given(@"a toast is shown")]
    public void GivenAToastIsShown() => throw new PendingStepException();

    [When(@"a few seconds pass")]
    public void WhenAFewSecondsPass() => throw new PendingStepException();

    [Then(@"the toast is gone")]
    public void ThenTheToastIsGone() => throw new PendingStepException();

    [When(@"a file operation fails")]
    public void WhenAFileOperationFails() => throw new PendingStepException();

    [Then(@"an error notification names the file that failed")]
    public void ThenAnErrorNotificationNamesTheFileThatFailed() => throw new PendingStepException();

    [When(@"two notifications arrive together")]
    public void WhenTwoNotificationsArriveTogether() => throw new PendingStepException();

    [Then(@"both are shown one above the other")]
    public void ThenBothAreShownOneAboveTheOther() => throw new PendingStepException();
}
