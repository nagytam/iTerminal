// Step bindings for Features/CommandPaletteAndHelp.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Application;

[Binding]
public sealed class CommandPaletteAndHelpSteps
{
    [Then(@"the palette lists commands such as {string} with the key {string}")]
    public void ThenThePaletteListsCommandsSuchAsValueWithTheKeyValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I press {string} and type {string}")]
    public void WhenIPressValueAndTypeValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"only commands matching {string} are listed")]
    public void ThenOnlyCommandsMatchingValueAreListed(string value1) => throw new PendingStepException();

    [Then(@"only commands whose text contains both words are listed")]
    public void ThenOnlyCommandsWhoseTextContainsBothWordsAreListed() => throw new PendingStepException();

    [Given(@"the left panel is focused and {string} is under the cursor")]
    public void GivenTheLeftPanelIsFocusedAndValueIsUnderTheCursor(string value1) => throw new PendingStepException();

    [When(@"I press {string} and choose {string} and press {string}")]
    public void WhenIPressValueAndChooseValueAndPressValue(string value1, string value2, string value3) => throw new PendingStepException();

    [Then(@"the rename dialog is shown for {string}")]
    public void ThenTheRenameDialogIsShownForValue(string value1) => throw new PendingStepException();

    [Given(@"the command {string} was run last")]
    public void GivenTheCommandValueWasRunLast(string value1) => throw new PendingStepException();

    [Then(@"{string} is listed first")]
    public void ThenValueIsListedFirst(string value1) => throw new PendingStepException();

    [Then(@"the help lists the keys that work here")]
    public void ThenTheHelpListsTheKeysThatWorkHere() => throw new PendingStepException();

    [Then(@"the About dialog shows the application version")]
    public void ThenTheAboutDialogShowsTheApplicationVersion() => throw new PendingStepException();

    [When(@"I open the Help menu")]
    public void WhenIOpenTheHelpMenu() => throw new PendingStepException();

    [Then(@"the menu lists {string}, {string} and {string}")]
    public void ThenTheMenuListsValueValueAndValue(string value1, string value2, string value3) => throw new PendingStepException();

    [When(@"I open {string} from the menu")]
    public void IOpenValueFromTheMenu(string value1) => throw new PendingStepException();

    [Then(@"the dialog shows the version of this build")]
    public void TheDialogShowsTheVersionOfThisBuild() => throw new PendingStepException();

    [Then(@"the dialog shows the copyright notice")]
    public void TheDialogShowsTheCopyrightNotice() => throw new PendingStepException();

    [Then(@"the dialog shows the colour map")]
    public void TheDialogShowsTheColourMap() => throw new PendingStepException();
}
