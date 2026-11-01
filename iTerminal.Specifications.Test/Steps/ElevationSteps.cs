// Step bindings for Features/Elevation.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class ElevationSteps
{
    [When(@"an action needs more rights, iTerminal can restart with them. Ctrl+Shift+R restarts it elevated.")]
    public void WhenAnActionNeedsMoreRightsITerminalCanRestartWithThemCtrlShiftRRestartsItElevated() => throw new PendingStepException();

    [Given(@"iTerminal runs without administrator rights")]
    public void GivenITerminalRunsWithoutAdministratorRights() => throw new PendingStepException();

    [When(@"I press {string} and confirm")]
    public void WhenIPressValueAndConfirm(string value1) => throw new PendingStepException();

    [Then(@"iTerminal restarts elevated")]
    public void ThenITerminalRestartsElevated() => throw new PendingStepException();

    [When(@"I press {string} and cancel the elevation prompt")]
    public void WhenIPressValueAndCancelTheElevationPrompt(string value1) => throw new PendingStepException();

    [Then(@"the current session is still running")]
    public void ThenTheCurrentSessionIsStillRunning() => throw new PendingStepException();

    [When(@"a format fails with {string}")]
    public void WhenAFormatFailsWithValue(string value1) => throw new PendingStepException();

    [Then(@"the message says to start iTerminal as administrator")]
    public void ThenTheMessageSaysToStartITerminalAsAdministrator() => throw new PendingStepException();
}
