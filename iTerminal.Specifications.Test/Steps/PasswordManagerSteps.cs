// Step bindings for Features/PasswordManager.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class PasswordManagerSteps
{
    [Then(@"the password manager lists the saved entries")]
    public void ThenThePasswordManagerListsTheSavedEntries() => throw new PendingStepException();

    [When(@"I press {string} and add an entry {string} with user {string} and a password")]
    public void WhenIPressValueAndAddAnEntryValueWithUserValueAndAPassword(string value1, string value2, string value3) => throw new PendingStepException();

    [Then(@"the password is not shown in the list")]
    public void ThenThePasswordIsNotShownInTheList() => throw new PendingStepException();

    [Given(@"a dialog is open")]
    public void GivenADialogIsOpen() => throw new PendingStepException();

    [Then(@"the password manager is shown")]
    public void ThenThePasswordManagerIsShown() => throw new PendingStepException();

    [When(@"I choose to copy the password of {string}")]
    public void WhenIChooseToCopyThePasswordOfValue(string value1) => throw new PendingStepException();

    [Then(@"the password is on the clipboard")]
    public void ThenThePasswordIsOnTheClipboard() => throw new PendingStepException();

    [When(@"I type {string} as an address")]
    public void WhenITypeValueAsAnAddress(string value1) => throw new PendingStepException();
}
