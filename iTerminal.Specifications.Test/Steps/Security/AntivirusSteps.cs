// Step bindings for Features/Security/Security.Antivirus.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Security;

[Binding]
public sealed class AntivirusSteps
{
    [When(@"I open the context menu")]
    public void IOpenTheContextMenu() => throw new PendingStepException();

    [Then(@"the context menu offers {string}")]
    public void TheContextMenuOffersValue(string value1) => throw new PendingStepException();

    [Then(@"the context menu does not offer {string}")]
    public void TheContextMenuDoesNotOfferValue(string value1) => throw new PendingStepException();

    [When(@"I move the cursor to a file in the archive")]
    public void IMoveTheCursorToAFileInTheArchive() => throw new PendingStepException();

    [Given(@"Defender finds no threat in {string}")]
    public void DefenderFindsNoThreatInValue(string value1) => throw new PendingStepException();

    [When(@"I scan {string} with Microsoft Defender")]
    public void IScanValueWithMicrosoftDefender(string value1) => throw new PendingStepException();

    [Then(@"the notification says {string} is clean")]
    public void TheNotificationSaysValueIsClean(string value1) => throw new PendingStepException();

    [Given(@"Defender finds a threat in {string}")]
    public void DefenderFindsAThreatInValue(string value1) => throw new PendingStepException();

    [Then(@"the notification names the threat found in {string}")]
    public void TheNotificationNamesTheThreatFoundInValue(string value1) => throw new PendingStepException();

    [Then(@"the notification says what Defender did with it")]
    public void TheNotificationSaysWhatDefenderDidWithIt() => throw new PendingStepException();

    [Given(@"Defender's command-line scanner is not installed")]
    public void DefenderSCommandLineScannerIsNotInstalled() => throw new PendingStepException();

    [Then(@"the notification says the Defender scanner could not be started")]
    public void TheNotificationSaysTheDefenderScannerCouldNotBeStarted() => throw new PendingStepException();
}
