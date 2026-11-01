// Step bindings for Features/DocumentManager.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class DocumentManagerSteps
{
    [Then(@"the document manager is shown with a search box")]
    public void ThenTheDocumentManagerIsShownWithASearchBox() => throw new PendingStepException();

    [Given(@"the index holds {string} containing {string}")]
    public void GivenTheIndexHoldsValueContainingValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I search for {string}")]
    public void WhenISearchForValue(string value1) => throw new PendingStepException();

    [Then(@"{string} is listed in the results")]
    public void ThenValueIsListedInTheResults(string value1) => throw new PendingStepException();

    [Given(@"the index holds {string} with the description {string}")]
    public void GivenTheIndexHoldsValueWithTheDescriptionValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"{string} is a scanned image without text")]
    public void GivenValueIsAScannedImageWithoutText(string value1) => throw new PendingStepException();

    [When(@"the index reads it")]
    public void WhenTheIndexReadsIt() => throw new PendingStepException();

    [Then(@"{string} is listed with no text extracted")]
    public void ThenValueIsListedWithNoTextExtracted(string value1) => throw new PendingStepException();

    [Given(@"the index holds {string} with the same size and date")]
    public void GivenTheIndexHoldsValueWithTheSameSizeAndDate(string value1) => throw new PendingStepException();

    [When(@"I rescan the folder")]
    public void WhenIRescanTheFolder() => throw new PendingStepException();

    [Then(@"{string} is not read again")]
    public void ThenValueIsNotReadAgain(string value1) => throw new PendingStepException();

    [Given(@"a search lists {string}")]
    public void GivenASearchListsValue(string value1) => throw new PendingStepException();

    [Then(@"the file opens in the viewer")]
    public void ThenTheFileOpensInTheViewer() => throw new PendingStepException();
}
