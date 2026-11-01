// Step bindings for Features/Printing.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class PrintingSteps
{
    [Given(@"a printer {string} is available")]
    public void GivenAPrinterValueIsAvailable(string value1) => throw new PendingStepException();

    [When(@"I choose {string} and press {string}")]
    public void WhenIChooseValueAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is sent to the printer {string}")]
    public void ThenValueIsSentToThePrinterValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"a job for {string} is queued on {string}")]
    public void GivenAJobForValueIsQueuedOnValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the queue lists the job for {string}")]
    public void ThenTheQueueListsTheJobForValue(string value1) => throw new PendingStepException();

    [Given(@"a job is queued on {string}")]
    public void GivenAJobIsQueuedOnValue(string value1) => throw new PendingStepException();

    [When(@"I choose {string} on the job")]
    public void WhenIChooseValueOnTheJob(string value1) => throw new PendingStepException();

    [Then(@"the job is held")]
    public void ThenTheJobIsHeld() => throw new PendingStepException();

    [Then(@"the job resumes")]
    public void ThenTheJobResumes() => throw new PendingStepException();

    [When(@"I choose {string} on the job and confirm")]
    public void WhenIChooseValueOnTheJobAndConfirm(string value1) => throw new PendingStepException();

    [Then(@"the job is removed from the queue")]
    public void ThenTheJobIsRemovedFromTheQueue() => throw new PendingStepException();

    [Given(@"the printer service does not answer within 15 seconds")]
    public void GivenThePrinterServiceDoesNotAnswerWithin15Seconds() => throw new PendingStepException();

    [Then(@"the dialog says the printer did not respond")]
    public void ThenTheDialogSaysThePrinterDidNotRespond() => throw new PendingStepException();

    [Given(@"{string} is on an ssh drive")]
    public void GivenValueIsOnAnSshDrive(string value1) => throw new PendingStepException();

    [When(@"I open the context menu on {string}")]
    public void WhenIOpenTheContextMenuOnValue(string value1) => throw new PendingStepException();

    [Then(@"{string} is not offered")]
    public void ThenValueIsNotOffered(string value1) => throw new PendingStepException();
}
