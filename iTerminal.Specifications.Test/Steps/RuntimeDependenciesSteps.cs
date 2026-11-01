// Step bindings for Features/RuntimeDependencies.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class RuntimeDependenciesSteps
{
    [Then(@"Playwright, Ollama and git are listed with {string} or {string}")]
    public void ThenPlaywrightOllamaAndGitAreListedWithValueOrValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"git is not installed")]
    public void GivenGitIsNotInstalled() => throw new PendingStepException();

    [Then(@"the install runs in the background with its progress shown")]
    public void ThenTheInstallRunsInTheBackgroundWithItsProgressShown() => throw new PendingStepException();

    [Given(@"Ollama is already running at its configured address")]
    public void GivenOllamaIsAlreadyRunningAtItsConfiguredAddress() => throw new PendingStepException();

    [When(@"I open the dependencies page")]
    public void WhenIOpenTheDependenciesPage() => throw new PendingStepException();

    [Then(@"Ollama is shown as available and nothing is downloaded")]
    public void ThenOllamaIsShownAsAvailableAndNothingIsDownloaded() => throw new PendingStepException();
}
