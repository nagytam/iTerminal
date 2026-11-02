// Step bindings for Features/MouseUse.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class MouseUseSteps
{
    [When(@"I click on {string} in the left panel")]
    public void WhenIClickOnValueInTheLeftPanel(string value1) => throw new PendingStepException();

    [When(@"I double-click {string} in the left panel")]
    public void WhenIDoubleClickValueInTheLeftPanel(string value1) => throw new PendingStepException();

    [Given(@"the left panel lists more entries than fit on screen")]
    public void GivenTheLeftPanelListsMoreEntriesThanFitOnScreen() => throw new PendingStepException();

    [When(@"I scroll the mouse wheel down in the left panel")]
    public void WhenIScrollTheMouseWheelDownInTheLeftPanel() => throw new PendingStepException();

    [Then(@"the list scrolls down")]
    public void ThenTheListScrollsDown() => throw new PendingStepException();

    [When(@"I click on {string} in the menu bar")]
    public void WhenIClickOnValueInTheMenuBar(string value1) => throw new PendingStepException();

    [Then(@"the File menu is open")]
    public void ThenTheFileMenuIsOpen() => throw new PendingStepException();

    [When(@"I click on {string} in the key bar")]
    public void WhenIClickOnValueInTheKeyBar(string value1) => throw new PendingStepException();

    [When(@"I right-click {string} in the left panel")]
    public void WhenIRightClickValueInTheLeftPanel(string value1) => throw new PendingStepException();

    [Then(@"the context menu for {string} is shown")]
    public void ThenTheContextMenuForValueIsShown(string value1) => throw new PendingStepException();

    [When(@"I click on the empty area of the screen")]
    public void WhenIClickOnTheEmptyAreaOfTheScreen() => throw new PendingStepException();

    [Then(@"the menu is closed")]
    public void ThenTheMenuIsClosed() => throw new PendingStepException();
}
