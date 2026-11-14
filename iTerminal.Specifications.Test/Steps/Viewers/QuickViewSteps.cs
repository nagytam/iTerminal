// Step bindings for Features/Viewers/Viewers.QuickView.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Viewers;

[Binding]
public sealed class QuickViewSteps
{
    [Then(@"the right panel shows a preview titled {string}")]
    public void TheRightPanelShowsAPreviewTitledValue(string value1) => throw new PendingStepException();

    [Given(@"the right panel is a quick view of the left panel")]
    public void TheRightPanelIsAQuickViewOfTheLeftPanel() => throw new PendingStepException();

    [Then(@"the right panel shows the number of entries in {string}")]
    public void TheRightPanelShowsTheNumberOfEntriesInValue(string value1) => throw new PendingStepException();

    [Given(@"{string} is a binary file of 2048 bytes")]
    public void ValueIsABinaryFileOf2048Bytes(string value1) => throw new PendingStepException();

    [Then(@"the right panel shows the size and kind of {string}")]
    public void TheRightPanelShowsTheSizeAndKindOfValue(string value1) => throw new PendingStepException();

    [Given(@"{string} is a text file of 500 MB")]
    public void ValueIsATextFileOf500MB(string value1) => throw new PendingStepException();

    [Then(@"the right panel does not read the whole file")]
    public void TheRightPanelDoesNotReadTheWholeFile() => throw new PendingStepException();
}
