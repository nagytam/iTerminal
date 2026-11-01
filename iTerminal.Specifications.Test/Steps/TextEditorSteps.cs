// Step bindings for Features/TextEditor.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class TextEditorSteps
{
    [Then(@"the editor shows {string}")]
    public void ThenTheEditorShowsValue(string value1) => throw new PendingStepException();

    [When(@"I type {string} at the start of the file")]
    public void WhenITypeValueAtTheStartOfTheFile(string value1) => throw new PendingStepException();

    [Then(@"{string} starts with {string}")]
    public void ThenValueStartsWithValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the editor shows no unsaved changes")]
    public void ThenTheEditorShowsNoUnsavedChanges() => throw new PendingStepException();

    [Given(@"the editor has unsaved changes to {string}")]
    public void GivenTheEditorHasUnsavedChangesToValue(string value1) => throw new PendingStepException();

    [Then(@"a prompt offers Save, Discard and Cancel")]
    public void ThenAPromptOffersSaveDiscardAndCancel() => throw new PendingStepException();

    [When(@"I press {string} and choose {string}")]
    public void WhenIPressValueAndChooseValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the file {string} is unchanged")]
    public void ThenTheFileValueIsUnchanged(string value1) => throw new PendingStepException();

    [Then(@"the editor is still open with its changes")]
    public void ThenTheEditorIsStillOpenWithItsChanges() => throw new PendingStepException();

    [Given(@"the editor is open on an empty line")]
    public void GivenTheEditorIsOpenOnAnEmptyLine() => throw new PendingStepException();

    [Then(@"four spaces are inserted")]
    public void ThenFourSpacesAreInserted() => throw new PendingStepException();

    [Given(@"the editor has an indented line")]
    public void GivenTheEditorHasAnIndentedLine() => throw new PendingStepException();

    [Then(@"the line loses one indent step")]
    public void ThenTheLineLosesOneIndentStep() => throw new PendingStepException();

    [Given(@"the cursor is after {string} in the editor")]
    public void GivenTheCursorIsAfterValueInTheEditor(string value1) => throw new PendingStepException();

    [Then(@"the new line starts with four spaces")]
    public void ThenTheNewLineStartsWithFourSpaces() => throw new PendingStepException();

    [Given(@"the editor has typed {string}")]
    public void GivenTheEditorHasTypedValue(string value1) => throw new PendingStepException();

    [Then(@"{string} is removed")]
    public void ThenValueIsRemoved(string value1) => throw new PendingStepException();

    [Then(@"{string} is typed again")]
    public void ThenValueIsTypedAgain(string value1) => throw new PendingStepException();

    [When(@"I replace {string} with {string}")]
    public void WhenIReplaceValueWithValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"the cursor is on line 10 of the editor")]
    public void GivenTheCursorIsOnLine10OfTheEditor() => throw new PendingStepException();

    [When(@"I set a bookmark on line 10")]
    public void WhenISetABookmarkOnLine10() => throw new PendingStepException();

    [When(@"I move the cursor to line 200")]
    public void WhenIMoveTheCursorToLine200() => throw new PendingStepException();

    [When(@"I jump to the bookmark")]
    public void WhenIJumpToTheBookmark() => throw new PendingStepException();

    [Then(@"the cursor is on line 10")]
    public void ThenTheCursorIsOnLine10() => throw new PendingStepException();

    [Given(@"{string} uses Windows line endings")]
    public void GivenValueUsesWindowsLineEndings(string value1) => throw new PendingStepException();

    [When(@"I edit and save {string}")]
    public void WhenIEditAndSaveValue(string value1) => throw new PendingStepException();

    [Then(@"{string} still uses Windows line endings")]
    public void ThenValueStillUsesWindowsLineEndings(string value1) => throw new PendingStepException();

    [Given(@"the editor shows a file of 300 lines")]
    public void GivenTheEditorShowsAFileOf300Lines() => throw new PendingStepException();

    [Then(@"line 120 is shown in the editor")]
    public void ThenLine120IsShownInTheEditor() => throw new PendingStepException();

    [Given(@"the editor shows a file with accented text")]
    public void GivenTheEditorShowsAFileWithAccentedText() => throw new PendingStepException();

    [Then(@"the text is shown under the next code page")]
    public void ThenTheTextIsShownUnderTheNextCodePage() => throw new PendingStepException();

    [Given(@"{string} is read-only")]
    public void GivenValueIsReadOnly(string value1) => throw new PendingStepException();

    [Then(@"the save is refused with a message")]
    public void ThenTheSaveIsRefusedWithAMessage() => throw new PendingStepException();

    [Then(@"the Python keywords are coloured")]
    public void ThenThePythonKeywordsAreColoured() => throw new PendingStepException();
}
