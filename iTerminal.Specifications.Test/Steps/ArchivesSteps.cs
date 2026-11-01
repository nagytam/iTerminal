// Step bindings for Features/Archives.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class ArchivesSteps
{
    [Given(@"{string} and {string} are marked in the left panel")]
    public void GivenValueAndValueAreMarkedInTheLeftPanel(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} holds {string} and {string}")]
    public void ThenValueHoldsValueAndValue(string value1, string value2, string value3) => throw new PendingStepException();

    [When(@"I choose {string} as the format")]
    public void WhenIChooseValueAsTheFormat(string value1) => throw new PendingStepException();

    [Then(@"the archive name changes from {string} to {string}")]
    public void ThenTheArchiveNameChangesFromValueToValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I type {string}")]
    public void WhenITypeValue(string value1) => throw new PendingStepException();

    [Then(@"the format is set to 7z")]
    public void ThenTheFormatIsSetTo7z() => throw new PendingStepException();

    [When(@"I choose {string}")]
    public void WhenIChooseValue(string value1) => throw new PendingStepException();

    [Then(@"the compression level control is disabled")]
    public void ThenTheCompressionLevelControlIsDisabled() => throw new PendingStepException();

    [When(@"I choose {string} and enter a password with AES-256")]
    public void WhenIChooseValueAndEnterAPasswordWithAES256(string value1) => throw new PendingStepException();

    [Then(@"the archive opens only with that password")]
    public void ThenTheArchiveOpensOnlyWithThatPassword() => throw new PendingStepException();

    [Given(@"{string} holds {string}")]
    public void GivenValueHoldsValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I add {string} to {string} and the operation fails")]
    public void WhenIAddValueToValueAndTheOperationFails(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is unchanged")]
    public void ThenValueIsUnchanged(string value1) => throw new PendingStepException();

    [Given(@"{string} is under the cursor in the left panel")]
    public void GivenValueIsUnderTheCursorInTheLeftPanel(string value1) => throw new PendingStepException();

    [Then(@"the target folder is the right panel's folder")]
    public void ThenTheTargetFolderIsTheRightPanelSFolder() => throw new PendingStepException();

    [When(@"I confirm the extraction")]
    public void WhenIConfirmTheExtraction() => throw new PendingStepException();

    [Then(@"the archive's entries are in a folder named {string} in the right panel's folder")]
    public void ThenTheArchiveSEntriesAreInAFolderNamedValueInTheRightPanelSFolder(string value1) => throw new PendingStepException();

    [Given(@"an archive holds an entry named {string}")]
    public void GivenAnArchiveHoldsAnEntryNamedValue(string value1) => throw new PendingStepException();

    [When(@"I extract the archive")]
    public void WhenIExtractTheArchive() => throw new PendingStepException();

    [Then(@"{string} is not written outside the target folder")]
    public void ThenValueIsNotWrittenOutsideTheTargetFolder(string value1) => throw new PendingStepException();

    [Then(@"the extraction reports the skipped entry")]
    public void ThenTheExtractionReportsTheSkippedEntry() => throw new PendingStepException();

    [Given(@"an encrypted archive {string} is under the cursor")]
    public void GivenAnEncryptedArchiveValueIsUnderTheCursor(string value1) => throw new PendingStepException();

    [When(@"I extract {string}")]
    public void WhenIExtractValue(string value1) => throw new PendingStepException();

    [Then(@"a password is asked for")]
    public void ThenAPasswordIsAskedFor() => throw new PendingStepException();

    [Given(@"the password typed for {string} is wrong")]
    public void GivenThePasswordTypedForValueIsWrong(string value1) => throw new PendingStepException();

    [Then(@"the message says the password is wrong")]
    public void ThenTheMessageSaysThePasswordIsWrong() => throw new PendingStepException();

    [Given(@"{string} is under the cursor")]
    public void GivenValueIsUnderTheCursor(string value1) => throw new PendingStepException();

    [Then(@"the left panel lists the entries of {string}")]
    public void ThenTheLeftPanelListsTheEntriesOfValue(string value1) => throw new PendingStepException();

    [Given(@"7-Zip is not installed")]
    public void Given7ZipIsNotInstalled() => throw new PendingStepException();

    [Then(@"the 7z format is not in the list and a hint says 7-Zip is needed")]
    public void ThenThe7zFormatIsNotInTheListAndAHintSays7ZipIsNeeded() => throw new PendingStepException();
}
