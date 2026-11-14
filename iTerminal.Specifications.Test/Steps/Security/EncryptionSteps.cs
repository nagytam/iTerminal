// Step bindings for Features/Security/Security.Encryption.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Security;

[Binding]
public sealed class EncryptionSteps
{
    [Then(@"the encryption dialog is open for {string}")]
    public void TheEncryptionDialogIsOpenForValue(string value1) => throw new PendingStepException();

    [When(@"I type the passphrase {string} twice and press {string}")]
    public void ITypeThePassphraseValueTwiceAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the dialog reports that the file is encrypted")]
    public void TheDialogReportsThatTheFileIsEncrypted() => throw new PendingStepException();

    [When(@"I press {string} and type the passphrase {string} twice")]
    public void IPressValueAndTypeThePassphraseValueTwice(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} and {string} are created")]
    public void ValueAndValueAreCreated(string value1, string value2) => throw new PendingStepException();

    [When(@"I type the passphrase {string} and then {string} in the two fields")]
    public void ITypeThePassphraseValueAndThenValueInTheTwoFields(string value1, string value2) => throw new PendingStepException();

    [Then(@"the dialog says the passphrases do not match")]
    public void TheDialogSaysThePassphrasesDoNotMatch() => throw new PendingStepException();

    [Then(@"{string} is not created")]
    public void ValueIsNotCreated(string value1) => throw new PendingStepException();

    [When(@"I type the passphrase {string} twice and choose to keep the originals")]
    public void ITypeThePassphraseValueTwiceAndChooseToKeepTheOriginals(string value1) => throw new PendingStepException();

    [When(@"I type the passphrase {string} twice and choose to delete the originals")]
    public void ITypeThePassphraseValueTwiceAndChooseToDeleteTheOriginals(string value1) => throw new PendingStepException();

    [Then(@"{string} is deleted")]
    public void ValueIsDeleted(string value1) => throw new PendingStepException();

    [Given(@"{string} was made from {string} with the passphrase {string}")]
    public void ValueWasMadeFromValueWithThePassphraseValue(string value1, string value2, string value3) => throw new PendingStepException();

    [When(@"I type the passphrase {string} and press {string}")]
    public void ITypeThePassphraseValueAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is restored with its original contents")]
    public void ValueIsRestoredWithItsOriginalContents(string value1) => throw new PendingStepException();

    [Then(@"the dialog says the passphrase is wrong")]
    public void TheDialogSaysThePassphraseIsWrong() => throw new PendingStepException();

    [Given(@"{string} has been changed since it was encrypted")]
    public void ValueHasBeenChangedSinceItWasEncrypted(string value1) => throw new PendingStepException();

    [Then(@"the dialog says the file has been altered")]
    public void TheDialogSaysTheFileHasBeenAltered() => throw new PendingStepException();

    [Given(@"a large file is being encrypted")]
    public void ALargeFileIsBeingEncrypted() => throw new PendingStepException();

    [Then(@"the encryption is stopped")]
    public void TheEncryptionIsStopped() => throw new PendingStepException();

    [Then(@"the partial output is removed")]
    public void ThePartialOutputIsRemoved() => throw new PendingStepException();
}
