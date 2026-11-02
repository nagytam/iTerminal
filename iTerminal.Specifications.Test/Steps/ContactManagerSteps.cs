// Step bindings for Features/ContactManager.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class ContactManagerSteps
{
    [Given(@"a contact {string} exists")]
    public void GivenAContactValueExists(string value1) => throw new PendingStepException();

    [When(@"I press {string} and add a contact {string} with an email address")]
    public void WhenIPressValueAndAddAContactValueWithAnEmailAddress(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is listed with the email address")]
    public void ThenValueIsListedWithTheEmailAddress(string value1) => throw new PendingStepException();

    [Given(@"the contacts {string} and {string} exist")]
    public void GivenTheContactsValueAndValueExist(string value1, string value2) => throw new PendingStepException();

    [When(@"I type {string} in the filter")]
    public void WhenITypeValueInTheFilter(string value1) => throw new PendingStepException();

    [Then(@"only {string} is listed")]
    public void ThenOnlyValueIsListed(string value1) => throw new PendingStepException();

    [Given(@"{string} is highlighted")]
    public void GivenValueIsHighlighted(string value1) => throw new PendingStepException();

    [When(@"I change the company of {string}")]
    public void WhenIChangeTheCompanyOfValue(string value1) => throw new PendingStepException();

    [Then(@"{string} is still highlighted")]
    public void ThenValueIsStillHighlighted(string value1) => throw new PendingStepException();

    [Given(@"the filter shows {string} only")]
    public void GivenTheFilterShowsValueOnly(string value1) => throw new PendingStepException();

    [When(@"I save a new contact {string} that the filter would hide")]
    public void WhenISaveANewContactValueThatTheFilterWouldHide(string value1) => throw new PendingStepException();

    [Then(@"the filter is cleared and {string} is shown")]
    public void ThenTheFilterIsClearedAndValueIsShown(string value1) => throw new PendingStepException();

    [Given(@"the contact {string} exists")]
    public void GivenTheContactValueExists(string value1) => throw new PendingStepException();

    [When(@"iTerminal is restarted")]
    public void WhenITerminalIsRestarted() => throw new PendingStepException();
}
