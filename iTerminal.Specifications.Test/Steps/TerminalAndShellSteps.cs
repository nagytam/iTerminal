// Step bindings for Features/TerminalAndShell.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class TerminalAndShellSteps
{
    [Then(@"a terminal is shown")]
    public void ThenATerminalIsShown() => throw new PendingStepException();

    [Then(@"the terminal is ready for input")]
    public void ThenTheTerminalIsReadyForInput() => throw new PendingStepException();

    [Given(@"the terminal is open")]
    public void GivenTheTerminalIsOpen() => throw new PendingStepException();

    [Then(@"the output {string} is shown in the terminal")]
    public void ThenTheOutputValueIsShownInTheTerminal(string value1) => throw new PendingStepException();

    [Then(@"the shell prompt is shown on the whole screen")]
    public void ThenTheShellPromptIsShownOnTheWholeScreen() => throw new PendingStepException();

    [Then(@"the file panels are hidden")]
    public void ThenTheFilePanelsAreHidden() => throw new PendingStepException();

    [Given(@"the shell was opened with Ctrl+O")]
    public void GivenTheShellWasOpenedWithCtrlO() => throw new PendingStepException();

    [Then(@"the interrupt goes to the shell and iTerminal keeps running")]
    public void ThenTheInterruptGoesToTheShellAndITerminalKeepsRunning() => throw new PendingStepException();

    [Then(@"the command log is shown")]
    public void ThenTheCommandLogIsShown() => throw new PendingStepException();

    [Then(@"it lists the commands run in this session")]
    public void ThenItListsTheCommandsRunInThisSession() => throw new PendingStepException();

    [Given(@"a file {string} has the terminal editor {string} as its edit program")]
    public void GivenAFileValueHasTheTerminalEditorValueAsItsEditProgram(string value1, string value2) => throw new PendingStepException();

    [Then(@"the terminal editor takes the whole terminal")]
    public void ThenTheTerminalEditorTakesTheWholeTerminal() => throw new PendingStepException();

    [Then(@"the panels are re-read when it exits")]
    public void ThenThePanelsAreReReadWhenItExits() => throw new PendingStepException();
}
