// Step bindings for Features/Developer/Developer.EnvironmentVariables.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Developer;

[Binding]
public sealed class EnvironmentVariablesSteps
{
    [Then(@"the environment variables editor is open")]
    public void TheEnvironmentVariablesEditorIsOpen() => throw new PendingStepException();

    [Then(@"its scope is {string}")]
    public void ItsScopeIsValue(string value1) => throw new PendingStepException();

    [When(@"I press {string} three times")]
    public void IPressValueThreeTimes(string value1) => throw new PendingStepException();

    [When(@"I add the variable {string} with the value {string}")]
    public void IAddTheVariableValueWithTheValueValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I start a program that prints {string}")]
    public void IStartAProgramThatPrintsValue(string value1) => throw new PendingStepException();

    [Then(@"the program prints {string}")]
    public void TheProgramPrintsValue(string value1) => throw new PendingStepException();

    [Given(@"I have added the Session variable {string} with the value {string}")]
    public void IHaveAddedTheSessionVariableValueWithTheValueValue(string value1, string value2) => throw new PendingStepException();

    [When(@"iTerminal is closed and started again")]
    public void ITerminalIsClosedAndStartedAgain() => throw new PendingStepException();

    [Then(@"{string} is not set")]
    public void ValueIsNotSet(string value1) => throw new PendingStepException();

    [Given(@"the environment variables editor is open with {string} in the list")]
    public void TheEnvironmentVariablesEditorIsOpenWithValueInTheList(string value1) => throw new PendingStepException();

    [Then(@"the footer asks whether to delete {string}")]
    public void TheFooterAsksWhetherToDeleteValue(string value1) => throw new PendingStepException();

    [Then(@"{string} is still listed until I answer yes")]
    public void ValueIsStillListedUntilIAnswerYes(string value1) => throw new PendingStepException();

    [When(@"I add a variable named {string} with the value {string}")]
    public void IAddAVariableNamedValueWithTheValueValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the form says the name is not valid")]
    public void TheFormSaysTheNameIsNotValid() => throw new PendingStepException();

    [Then(@"the form is still open")]
    public void TheFormIsStillOpen() => throw new PendingStepException();

    [Given(@"a User variable was changed outside iTerminal")]
    public void AUserVariableWasChangedOutsideITerminal() => throw new PendingStepException();

    [Then(@"the list shows the changed value")]
    public void TheListShowsTheChangedValue() => throw new PendingStepException();

    [Then(@"only variables whose name or value contains {string} are listed")]
    public void OnlyVariablesWhoseNameOrValueContainsValueAreListed(string value1) => throw new PendingStepException();
}
