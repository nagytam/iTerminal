// Step bindings for Features/Documents/Documents.Scanner.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Documents;

[Binding]
public sealed class ScannerSteps
{
    [Given(@"two scanners are connected")]
    public void TwoScannersAreConnected() => throw new PendingStepException();

    [Then(@"the dialog lists both scanners")]
    public void TheDialogListsBothScanners() => throw new PendingStepException();

    [Given(@"no scanner is connected")]
    public void NoScannerIsConnected() => throw new PendingStepException();

    [Then(@"the dialog says that no scanner was found")]
    public void TheDialogSaysThatNoScannerWasFound() => throw new PendingStepException();

    [Then(@"the dialog offers to search again")]
    public void TheDialogOffersToSearchAgain() => throw new PendingStepException();

    [Given(@"a scanner is connected")]
    public void AScannerIsConnected() => throw new PendingStepException();

    [When(@"I scan one page in the scanner dialog")]
    public void IScanOnePageInTheScannerDialog() => throw new PendingStepException();

    [Then(@"the scanned page is shown in the dialog")]
    public void TheScannedPageIsShownInTheDialog() => throw new PendingStepException();

    [Then(@"no file has been saved")]
    public void NoFileHasBeenSaved() => throw new PendingStepException();

    [Given(@"a scanner with a document feeder holds three pages")]
    public void AScannerWithADocumentFeederHoldsThreePages() => throw new PendingStepException();

    [When(@"I scan the whole feeder in the scanner dialog")]
    public void IScanTheWholeFeederInTheScannerDialog() => throw new PendingStepException();

    [Then(@"three pages are shown in the dialog")]
    public void ThreePagesAreShownInTheDialog() => throw new PendingStepException();

    [Given(@"I have scanned one page in the scanner dialog")]
    public void IHaveScannedOnePageInTheScannerDialog() => throw new PendingStepException();

    [When(@"I save the scan as {string}")]
    public void ISaveTheScanAsValue(string value1) => throw new PendingStepException();

    [Then(@"the saved scan is created in the folder of the left panel")]
    public void TheSavedScanIsCreatedInTheFolderOfTheLeftPanel() => throw new PendingStepException();
}
