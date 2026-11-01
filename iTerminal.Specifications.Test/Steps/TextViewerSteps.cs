// Step bindings for Features/TextViewer.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class TextViewerSteps
{
    [Then(@"the viewer shows the first lines of {string}")]
    public void ThenTheViewerShowsTheFirstLinesOfValue(string value1) => throw new PendingStepException();

    [Given(@"the viewer shows {string}")]
    public void GivenTheViewerShowsValue(string value1) => throw new PendingStepException();

    [Then(@"the viewer is closed")]
    public void ThenTheViewerIsClosed() => throw new PendingStepException();

    [Given(@"the viewer shows a file with a line longer than the screen")]
    public void GivenTheViewerShowsAFileWithALineLongerThanTheScreen() => throw new PendingStepException();

    [Then(@"the long line is broken across rows")]
    public void ThenTheLongLineIsBrokenAcrossRows() => throw new PendingStepException();

    [Given(@"the viewer shows a file of 500 lines")]
    public void GivenTheViewerShowsAFileOf500Lines() => throw new PendingStepException();

    [Then(@"line 250 is shown a third of the way down the view")]
    public void ThenLine250IsShownAThirdOfTheWayDownTheView() => throw new PendingStepException();

    [Given(@"the viewer shows a file with the word {string} on three lines")]
    public void GivenTheViewerShowsAFileWithTheWordValueOnThreeLines(string value1) => throw new PendingStepException();

    [Then(@"the first hit is shown")]
    public void ThenTheFirstHitIsShown() => throw new PendingStepException();

    [Then(@"the next hit is shown")]
    public void ThenTheNextHitIsShown() => throw new PendingStepException();

    [Given(@"the viewer shows a long file")]
    public void GivenTheViewerShowsALongFile() => throw new PendingStepException();

    [Then(@"the last line is at the bottom of the view")]
    public void ThenTheLastLineIsAtTheBottomOfTheView() => throw new PendingStepException();

    [Then(@"the first line is at the top of the view")]
    public void ThenTheFirstLineIsAtTheTopOfTheView() => throw new PendingStepException();

    [When(@"I scroll the mouse wheel down once")]
    public void WhenIScrollTheMouseWheelDownOnce() => throw new PendingStepException();

    [Then(@"the view moves down three rows")]
    public void ThenTheViewMovesDownThreeRows() => throw new PendingStepException();

    [Given(@"the viewer shows a file with accented text in Windows-1250")]
    public void GivenTheViewerShowsAFileWithAccentedTextInWindows1250() => throw new PendingStepException();

    [Then(@"the text is shown under the previous code page")]
    public void ThenTheTextIsShownUnderThePreviousCodePage() => throw new PendingStepException();

    [Then(@"the file is shown as bytes with offsets")]
    public void ThenTheFileIsShownAsBytesWithOffsets() => throw new PendingStepException();

    [Then(@"the view goes to byte offset 16")]
    public void ThenTheViewGoesToByteOffset16() => throw new PendingStepException();

    [Then(@"the keywords of the C# language are coloured")]
    public void ThenTheKeywordsOfTheCLanguageAreColoured() => throw new PendingStepException();

    [Then(@"the right panel shows the first lines of {string}")]
    public void ThenTheRightPanelShowsTheFirstLinesOfValue(string value1) => throw new PendingStepException();

    [Then(@"pressing {string} again returns the right panel to its folder")]
    public void ThenPressingValueAgainReturnsTheRightPanelToItsFolder(string value1) => throw new PendingStepException();

    [Given(@"the viewer opens {string} as a picture or as bytes")]
    public void GivenTheViewerOpensValueAsAPictureOrAsBytes(string value1) => throw new PendingStepException();

    [Then(@"the file is not shown as garbled text")]
    public void ThenTheFileIsNotShownAsGarbledText() => throw new PendingStepException();

    [When(@"I view {string} with F3")]
    public void WhenIViewValueWithF3(string value1) => throw new PendingStepException();

    [Then(@"the modification date of {string} is unchanged")]
    public void ThenTheModificationDateOfValueIsUnchanged(string value1) => throw new PendingStepException();
}
