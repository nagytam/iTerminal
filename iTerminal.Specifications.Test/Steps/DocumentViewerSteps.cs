// Step bindings for Features/DocumentViewer.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class DocumentViewerSteps
{
    [Then(@"the viewer shows the text of {string}")]
    public void ThenTheViewerShowsTheTextOfValue(string value1) => throw new PendingStepException();

    [Then(@"each sheet's cells are shown as text")]
    public void ThenEachSheetSCellsAreShownAsText() => throw new PendingStepException();

    [Then(@"the text of {string} is shown")]
    public void ThenTheTextOfValueIsShown(string value1) => throw new PendingStepException();

    [Then(@"the chapters of the e-book are shown in reading order")]
    public void ThenTheChaptersOfTheEBookAreShownInReadingOrder() => throw new PendingStepException();

    [Then(@"the headers and body of the message are shown with the subject decoded")]
    public void ThenTheHeadersAndBodyOfTheMessageAreShownWithTheSubjectDecoded() => throw new PendingStepException();

    [Then(@"the sender, subject and body of the message are shown")]
    public void ThenTheSenderSubjectAndBodyOfTheMessageAreShown() => throw new PendingStepException();

    [Given(@"{string} is not a valid document")]
    public void GivenValueIsNotAValidDocument(string value1) => throw new PendingStepException();

    [Then(@"the viewer says the document could not be read")]
    public void ThenTheViewerSaysTheDocumentCouldNotBeRead() => throw new PendingStepException();
}
