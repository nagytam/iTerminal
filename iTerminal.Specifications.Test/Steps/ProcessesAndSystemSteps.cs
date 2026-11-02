// Step bindings for Features/ProcessesAndSystem.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class ProcessesAndSystemSteps
{
    [Then(@"the process list shows each process with its name and memory")]
    public void ThenTheProcessListShowsEachProcessWithItsNameAndMemory() => throw new PendingStepException();

    [Given(@"the process list is open")]
    public void GivenTheProcessListIsOpen() => throw new PendingStepException();

    [Then(@"the processes are sorted by memory")]
    public void ThenTheProcessesAreSortedByMemory() => throw new PendingStepException();

    [Given(@"the process list shows {string}")]
    public void GivenTheProcessListShowsValue(string value1) => throw new PendingStepException();

    [When(@"I choose {string} on {string}")]
    public void WhenIChooseValueOnValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the dialog asks whether to end {string}")]
    public void ThenTheDialogAsksWhetherToEndValue(string value1) => throw new PendingStepException();

    [Then(@"{string} keeps running")]
    public void ThenValueKeepsRunning(string value1) => throw new PendingStepException();

    [Given(@"a copy is running")]
    public void GivenACopyIsRunning() => throw new PendingStepException();

    [Then(@"the task queue lists the copy with its progress")]
    public void ThenTheTaskQueueListsTheCopyWithItsProgress() => throw new PendingStepException();

    [Given(@"the task queue lists a queued copy")]
    public void GivenTheTaskQueueListsAQueuedCopy() => throw new PendingStepException();

    [Then(@"the copy does not start until it is resumed")]
    public void ThenTheCopyDoesNotStartUntilItIsResumed() => throw new PendingStepException();

    [Given(@"the task queue lists a running copy")]
    public void GivenTheTaskQueueListsARunningCopy() => throw new PendingStepException();

    [Then(@"the copy is cancelled")]
    public void ThenTheCopyIsCancelled() => throw new PendingStepException();

    [Given(@"two queued tasks are listed")]
    public void GivenTwoQueuedTasksAreListed() => throw new PendingStepException();

    [When(@"I press {string} on the second task")]
    public void WhenIPressValueOnTheSecondTask(string value1) => throw new PendingStepException();

    [Then(@"it is listed first")]
    public void ThenItIsListedFirst() => throw new PendingStepException();

    [Then(@"the dialog shows the operating system, processor and memory")]
    public void ThenTheDialogShowsTheOperatingSystemProcessorAndMemory() => throw new PendingStepException();

    [When(@"I open the process list on a busy machine")]
    public void WhenIOpenTheProcessListOnABusyMachine() => throw new PendingStepException();

    [Then(@"the screen keeps responding while the list loads")]
    public void ThenTheScreenKeepsRespondingWhileTheListLoads() => throw new PendingStepException();
}
