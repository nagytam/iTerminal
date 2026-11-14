// Step bindings for Features/Files/Files.Streams.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Files;

[Binding]
public sealed class StreamsSteps
{
    [Given(@"{string} has an extra stream {string} of 26 bytes")]
    public void ValueHasAnExtraStreamValueOf26Bytes(string value1, string value2) => throw new PendingStepException();

    [When(@"I open {string} from the context menu")]
    public void IOpenValueFromTheContextMenu(string value1) => throw new PendingStepException();

    [Then(@"the dialog lists {string} with 26 bytes")]
    public void TheDialogListsValueWith26Bytes(string value1) => throw new PendingStepException();

    [Given(@"the streams dialog is open for {string}")]
    public void TheStreamsDialogIsOpenForValue(string value1) => throw new PendingStepException();

    [When(@"I move the cursor to {string} in the dialog")]
    public void IMoveTheCursorToValueInTheDialog(string value1) => throw new PendingStepException();

    [Then(@"the preview shows the text of {string}")]
    public void ThePreviewShowsTheTextOfValue(string value1) => throw new PendingStepException();

    [Then(@"the preview shows the bytes of {string} in hex")]
    public void ThePreviewShowsTheBytesOfValueInHex(string value1) => throw new PendingStepException();

    [When(@"I press {string} on {string} and confirm")]
    public void IPressValueOnValueAndConfirm(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} no longer has a stream named {string}")]
    public void ValueNoLongerHasAStreamNamedValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"{string} has no extra streams or attributes")]
    public void ValueHasNoExtraStreamsOrAttributes(string value1) => throw new PendingStepException();

    [Then(@"the dialog says that there are no streams or attributes")]
    public void TheDialogSaysThatThereAreNoStreamsOrAttributes() => throw new PendingStepException();
}
