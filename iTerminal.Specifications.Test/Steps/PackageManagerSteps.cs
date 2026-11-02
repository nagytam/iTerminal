// Step bindings for Features/PackageManager.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class PackageManagerSteps
{
    [Then(@"the installed packages are listed with their versions")]
    public void ThenTheInstalledPackagesAreListedWithTheirVersions() => throw new PendingStepException();

    [Then(@"{string} is listed with its description")]
    public void ThenValueIsListedWithItsDescription(string value1) => throw new PendingStepException();

    [When(@"I choose to install {string}")]
    public void WhenIChooseToInstallValue(string value1) => throw new PendingStepException();

    [Then(@"the dialog asks for confirmation before anything is installed")]
    public void ThenTheDialogAsksForConfirmationBeforeAnythingIsInstalled() => throw new PendingStepException();

    [Given(@"the user confirmed the install of {string}")]
    public void GivenTheUserConfirmedTheInstallOfValue(string value1) => throw new PendingStepException();

    [Then(@"the install runs in the background and its output is kept")]
    public void ThenTheInstallRunsInTheBackgroundAndItsOutputIsKept() => throw new PendingStepException();

    [Given(@"apt is not installed")]
    public void GivenAptIsNotInstalled() => throw new PendingStepException();

    [Then(@"the dialog says the package manager is not available")]
    public void ThenTheDialogSaysThePackageManagerIsNotAvailable() => throw new PendingStepException();
}
