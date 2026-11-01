// Step bindings for Features/CommandLine.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class CommandLineSteps
{
    [Then(@"the command line holds {string}")]
    public void ThenTheCommandLineHoldsValue(string value1) => throw new PendingStepException();

    [Given(@"a file named {string} is under the cursor")]
    public void GivenAFileNamedValueIsUnderTheCursor(string value1) => throw new PendingStepException();

    [Then(@"the command line holds the name of the file in double quotes")]
    public void ThenTheCommandLineHoldsTheNameOfTheFileInDoubleQuotes() => throw new PendingStepException();

    [Then(@"the command line holds the path of the left panel's folder")]
    public void ThenTheCommandLineHoldsThePathOfTheLeftPanelSFolder() => throw new PendingStepException();

    [When(@"I press {string} in the left panel")]
    public void WhenIPressValueInTheLeftPanel(string value1) => throw new PendingStepException();

    [Then(@"the command {string} runs in the command log")]
    public void ThenTheCommandValueRunsInTheCommandLog(string value1) => throw new PendingStepException();

    [Then(@"the panel does not open {string}")]
    public void ThenThePanelDoesNotOpenValue(string value1) => throw new PendingStepException();

    [Given(@"the command line is empty")]
    public void GivenTheCommandLineIsEmpty() => throw new PendingStepException();

    [Given(@"the commands {string} and {string} were run")]
    public void GivenTheCommandsValueAndValueWereRun(string value1, string value2) => throw new PendingStepException();

    [Given(@"the command {string} was run")]
    public void GivenTheCommandValueWasRun(string value1) => throw new PendingStepException();

    [Then(@"an error line says that {string} does not exist")]
    public void ThenAnErrorLineSaysThatValueDoesNotExist(string value1) => throw new PendingStepException();

    [Then(@"the left panel still lists {string}")]
    public void ThenTheLeftPanelStillListsValue(string value1) => throw new PendingStepException();

    [Then(@"the command runs in the shell and not as a panel change")]
    public void ThenTheCommandRunsInTheShellAndNotAsAPanelChange() => throw new PendingStepException();

    [Given(@"a command is already running")]
    public void GivenACommandIsAlreadyRunning() => throw new PendingStepException();

    [When(@"I move the cursor to {string} and press {string} in the left panel")]
    public void WhenIMoveTheCursorToValueAndPressValueInTheLeftPanel(string value1, string value2) => throw new PendingStepException();

    [Then(@"the new command is not started")]
    public void ThenTheNewCommandIsNotStarted() => throw new PendingStepException();
}
