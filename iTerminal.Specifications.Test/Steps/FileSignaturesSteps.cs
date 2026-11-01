// Step bindings for Features/FileSignatures.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class FileSignaturesSteps
{
    [Given(@"{string} has an OpenPGP signature {string}")]
    public void GivenValueHasAnOpenPGPSignatureValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the dialog shows a verdict for the OpenPGP signature")]
    public void ThenTheDialogShowsAVerdictForTheOpenPGPSignature() => throw new PendingStepException();

    [Then(@"the dialog says the file is not signed")]
    public void ThenTheDialogSaysTheFileIsNotSigned() => throw new PendingStepException();

    [Given(@"gpg is installed and has a default key")]
    public void GivenGpgIsInstalledAndHasADefaultKey() => throw new PendingStepException();

    [Given(@"{string} already exists")]
    public void GivenValueAlreadyExists(string value1) => throw new PendingStepException();

    [When(@"I press {string} in the signature dialog")]
    public void WhenIPressValueInTheSignatureDialog(string value1) => throw new PendingStepException();

    [Then(@"{string} is not overwritten")]
    public void ThenValueIsNotOverwritten(string value1) => throw new PendingStepException();

    [Then(@"the dialog says a signature file already exists")]
    public void ThenTheDialogSaysASignatureFileAlreadyExists() => throw new PendingStepException();

    [When(@"I type {string} as the key and press {string}")]
    public void WhenITypeValueAsTheKeyAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the signing is refused and nothing runs")]
    public void ThenTheSigningIsRefusedAndNothingRuns() => throw new PendingStepException();

    [Given(@"the signature dialog is open for a signed file")]
    public void GivenTheSignatureDialogIsOpenForASignedFile() => throw new PendingStepException();

    [Then(@"the verdict is shown again")]
    public void ThenTheVerdictIsShownAgain() => throw new PendingStepException();

    [Given(@"the signature dialog is verifying a file")]
    public void GivenTheSignatureDialogIsVerifyingAFile() => throw new PendingStepException();

    [Then(@"the verification stops")]
    public void ThenTheVerificationStops() => throw new PendingStepException();
}
