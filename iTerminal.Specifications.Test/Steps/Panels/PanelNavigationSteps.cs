// Step bindings for Features/Panels/Panels.PanelNavigation.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Panels;

[Binding]
public sealed class PanelNavigationSteps
{
    [When(@"I open Go To Folder from the menu")]
    public void IOpenGoToFolderFromTheMenu() => throw new PendingStepException();

    [When(@"I type the path of {string} and press {string}")]
    public void ITypeThePathOfValueAndPressValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I type a path that does not exist and press {string}")]
    public void ITypeAPathThatDoesNotExistAndPressValue(string value1) => throw new PendingStepException();

    [Then(@"the dialog says that the folder does not exist")]
    public void TheDialogSaysThatTheFolderDoesNotExist() => throw new PendingStepException();

    [Then(@"the left panel still lists its previous folder")]
    public void TheLeftPanelStillListsItsPreviousFolder() => throw new PendingStepException();
}
