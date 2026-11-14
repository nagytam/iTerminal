// Step bindings for Features/Checksums.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Files;

[Binding]
public sealed class ChecksumsSteps
{
    [Then(@"the dialog shows the MD5, SHA-1, SHA-256 and SHA-512 of {string}")]
    public void ThenTheDialogShowsTheMD5SHA1SHA256AndSHA512OfValue(string value1) => throw new PendingStepException();

    [Given(@"the checksum dialog is open for {string}")]
    public void GivenTheChecksumDialogIsOpenForValue(string value1) => throw new PendingStepException();

    [When(@"I type the SHA-256 of {string}")]
    public void WhenITypeTheSHA256OfValue(string value1) => throw new PendingStepException();

    [Then(@"the dialog says the checksums match")]
    public void ThenTheDialogSaysTheChecksumsMatch() => throw new PendingStepException();

    [When(@"I type a SHA-256 that differs from the file")]
    public void WhenITypeASHA256ThatDiffersFromTheFile() => throw new PendingStepException();

    [Then(@"the dialog says the checksums do not match")]
    public void ThenTheDialogSaysTheChecksumsDoNotMatch() => throw new PendingStepException();

    [When(@"I type the SHA-256 with spaces between each pair of digits")]
    public void WhenITypeTheSHA256WithSpacesBetweenEachPairOfDigits() => throw new PendingStepException();

    [Given(@"{string} holds the checksum of {string}")]
    public void GivenValueHoldsTheChecksumOfValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I move the cursor to {string} and press {string}")]
    public void WhenIMoveTheCursorToValueAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the dialog says that the file matches {string}")]
    public void ThenTheDialogSaysThatTheFileMatchesValue(string value1) => throw new PendingStepException();

    [Given(@"a {string} list in the folder holds the checksum of {string}")]
    public void GivenAValueListInTheFolderHoldsTheChecksumOfValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the dialog says the file matches the list")]
    public void ThenTheDialogSaysTheFileMatchesTheList() => throw new PendingStepException();

    [Then(@"{string} is created")]
    public void ThenValueIsCreated(string value1) => throw new PendingStepException();

    [Given(@"{string} holds a different checksum")]
    public void GivenValueHoldsADifferentChecksum(string value1) => throw new PendingStepException();

    [When(@"I press {string} in the checksum dialog for {string}")]
    public void WhenIPressValueInTheChecksumDialogForValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is not changed")]
    public void ThenValueIsNotChanged(string value1) => throw new PendingStepException();

    [Then(@"the dialog says the existing file holds a different checksum")]
    public void ThenTheDialogSaysTheExistingFileHoldsADifferentChecksum() => throw new PendingStepException();

    [Given(@"the left panel shows the inside of a ZIP archive")]
    public void GivenTheLeftPanelShowsTheInsideOfAZIPArchive() => throw new PendingStepException();

    [When(@"I move the cursor to a file in the archive and press {string}")]
    public void WhenIMoveTheCursorToAFileInTheArchiveAndPressValue(string value1) => throw new PendingStepException();

    [Then(@"the four checksums of that file are shown")]
    public void ThenTheFourChecksumsOfThatFileAreShown() => throw new PendingStepException();

    [Given(@"a large file's checksum is being computed")]
    public void GivenALargeFileSChecksumIsBeingComputed() => throw new PendingStepException();

    [Then(@"the computation is cancelled")]
    public void ThenTheComputationIsCancelled() => throw new PendingStepException();

    [When(@"I choose to write the manifest")]
    public void IChooseToWriteTheManifest() => throw new PendingStepException();

    [Then(@"{string} lists the SHA-256 of {string} and {string}")]
    public void ValueListsTheSHA256OfValueAndValue(string value1, string value2, string value3) => throw new PendingStepException();

    [Given(@"the folder {string} holds {string} and {string}")]
    public void TheFolderValueHoldsValueAndValue(string value1, string value2, string value3) => throw new PendingStepException();

    [When(@"I move the cursor to {string} in the left panel and press {string}")]
    public void IMoveTheCursorToValueInTheLeftPanelAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} in {string} lists both files with paths relative to {string}")]
    public void ValueInValueListsBothFilesWithPathsRelativeToValue(string value1, string value2, string value3) => throw new PendingStepException();

    [Given(@"{string} has a SHA256SUMS manifest and {string} has been changed")]
    public void ValueHasASHA256SUMSManifestAndValueHasBeenChanged(string value1, string value2) => throw new PendingStepException();

    [When(@"I check {string} against its manifest")]
    public void ICheckValueAgainstItsManifest(string value1) => throw new PendingStepException();

    [Then(@"the report names {string} as changed")]
    public void TheReportNamesValueAsChanged(string value1) => throw new PendingStepException();

    [Given(@"{string} has a SHA256SUMS manifest and {string} was added")]
    public void ValueHasASHA256SUMSManifestAndValueWasAdded(string value1, string value2) => throw new PendingStepException();

    [Then(@"the report names {string} as not in the manifest")]
    public void TheReportNamesValueAsNotInTheManifest(string value1) => throw new PendingStepException();

    [Then(@"the browser or lookup shows the reputation of the SHA-256 of {string}")]
    public void TheBrowserOrLookupShowsTheReputationOfTheSHA256OfValue(string value1) => throw new PendingStepException();

    [Then(@"the file itself is not sent anywhere")]
    public void TheFileItselfIsNotSentAnywhere() => throw new PendingStepException();

    [Given(@"the checksum dialog is open for a file no engine has seen")]
    public void TheChecksumDialogIsOpenForAFileNoEngineHasSeen() => throw new PendingStepException();

    [Then(@"the dialog says the hash is not known to the reputation service")]
    public void TheDialogSaysTheHashIsNotKnownToTheReputationService() => throw new PendingStepException();
}
