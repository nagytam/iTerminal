// Step bindings for Features/Developer/Developer.SystemSettings.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Developer;

[Binding]
public sealed class SystemSettingsSteps
{
    [Then(@"the system settings dialog is open")]
    public void TheSystemSettingsDialogIsOpen() => throw new PendingStepException();

    [Then(@"the dialog shows the machine's name")]
    public void TheDialogShowsTheMachineSName() => throw new PendingStepException();

    [Then(@"the dialog shows the environment variables section")]
    public void TheDialogShowsTheEnvironmentVariablesSection() => throw new PendingStepException();

    [Then(@"the dialog shows the machine's name section")]
    public void TheDialogShowsTheMachineSNameSection() => throw new PendingStepException();

    [Given(@"the system settings dialog is open on the machine's name")]
    public void TheSystemSettingsDialogIsOpenOnTheMachineSName() => throw new PendingStepException();

    [Then(@"the machine's name is {string}")]
    public void TheMachineSNameIsValue(string value1) => throw new PendingStepException();

    [Given(@"iTerminal is not running with administrator rights")]
    public void ITerminalIsNotRunningWithAdministratorRights() => throw new PendingStepException();

    [Then(@"the bottom row says that administrator rights are needed")]
    public void TheBottomRowSaysThatAdministratorRightsAreNeeded() => throw new PendingStepException();

    [Then(@"the dialog shows at the top that it has no administrator rights")]
    public void TheDialogShowsAtTheTopThatItHasNoAdministratorRights() => throw new PendingStepException();

    [When(@"I press {string} until the time zone section is shown")]
    public void IPressValueUntilTheTimeZoneSectionIsShown(string value1) => throw new PendingStepException();

    [Then(@"the dialog shows the time zone in use")]
    public void TheDialogShowsTheTimeZoneInUse() => throw new PendingStepException();
}
