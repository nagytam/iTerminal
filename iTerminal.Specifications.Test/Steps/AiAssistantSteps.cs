// Step bindings for Features/AiAssistant.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class AiAssistantSteps
{
    [Then(@"the AI model dialog lists the installed models")]
    public void ThenTheAIModelDialogListsTheInstalledModels() => throw new PendingStepException();

    [Given(@"the AI bar is shown with the model {string} selected")]
    public void GivenTheAIBarIsShownWithTheModelValueSelected(string value1) => throw new PendingStepException();

    [When(@"I type {string} and press {string}")]
    public void WhenITypeValueAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the answer appears in the AI bar")]
    public void ThenTheAnswerAppearsInTheAIBar() => throw new PendingStepException();

    [Then(@"the screen keeps responding while the answer is written")]
    public void ThenTheScreenKeepsRespondingWhileTheAnswerIsWritten() => throw new PendingStepException();

    [Given(@"no Ollama model is installed")]
    public void GivenNoOllamaModelIsInstalled() => throw new PendingStepException();

    [When(@"I choose to set up the AI model")]
    public void WhenIChooseToSetUpTheAIModel() => throw new PendingStepException();

    [Then(@"the model is downloaded with progress shown")]
    public void ThenTheModelIsDownloadedWithProgressShown() => throw new PendingStepException();

    [Given(@"the left panel shows {string}, {string} and {string}")]
    public void GivenTheLeftPanelShowsValueValueAndValue(string value1, string value2, string value3) => throw new PendingStepException();

    [When(@"I ask {string}")]
    public void WhenIAskValue(string value1) => throw new PendingStepException();

    [Then(@"the question sent to the model names the folder's entries")]
    public void ThenTheQuestionSentToTheModelNamesTheFolderSEntries() => throw new PendingStepException();

    [Given(@"Ollama is not running and cannot be started")]
    public void GivenOllamaIsNotRunningAndCannotBeStarted() => throw new PendingStepException();

    [When(@"I type a question and press {string}")]
    public void WhenITypeAQuestionAndPressValue(string value1) => throw new PendingStepException();

    [Then(@"the AI bar says that no model is available")]
    public void ThenTheAIBarSaysThatNoModelIsAvailable() => throw new PendingStepException();

    [When(@"I press {string} until the command line is shown")]
    public void WhenIPressValueUntilTheCommandLineIsShown(string value1) => throw new PendingStepException();

    [Then(@"the AI bar is hidden and the command line is shown")]
    public void ThenTheAIBarIsHiddenAndTheCommandLineIsShown() => throw new PendingStepException();
}
