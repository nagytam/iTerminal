// Step bindings for Features/TranslatorAndEncyclopedia.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class TranslatorAndEncyclopediaSteps
{
    [When(@"I type {string} and choose {string}")]
    public void WhenITypeValueAndChooseValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the translation {string} is shown")]
    public void ThenTheTranslationValueIsShown(string value1) => throw new PendingStepException();

    [Then(@"the source language is detected as {string}")]
    public void ThenTheSourceLanguageIsDetectedAsValue(string value1) => throw new PendingStepException();

    [When(@"I type a text of several paragraphs and translate it")]
    public void WhenITypeATextOfSeveralParagraphsAndTranslateIt() => throw new PendingStepException();

    [Then(@"the translation keeps the paragraphs in their order")]
    public void ThenTheTranslationKeepsTheParagraphsInTheirOrder() => throw new PendingStepException();

    [When(@"I translate a text")]
    public void WhenITranslateAText() => throw new PendingStepException();

    [Then(@"the screen keeps responding until the translation arrives")]
    public void ThenTheScreenKeepsRespondingUntilTheTranslationArrives() => throw new PendingStepException();

    [Given(@"no translation service is configured")]
    public void GivenNoTranslationServiceIsConfigured() => throw new PendingStepException();

    [When(@"I translate {string}")]
    public void WhenITranslateValue(string value1) => throw new PendingStepException();

    [Then(@"the translator says that no translation service is available")]
    public void ThenTheTranslatorSaysThatNoTranslationServiceIsAvailable() => throw new PendingStepException();

    [Then(@"the matching articles are listed")]
    public void ThenTheMatchingArticlesAreListed() => throw new PendingStepException();

    [Then(@"the article under the cursor is shown on the right")]
    public void ThenTheArticleUnderTheCursorIsShownOnTheRight() => throw new PendingStepException();

    [Then(@"a dictionary entry for {string} is shown")]
    public void ThenADictionaryEntryForValueIsShown(string value1) => throw new PendingStepException();

    [Then(@"the term opens in the system's browser and no page is fetched by iTerminal")]
    public void ThenTheTermOpensInTheSystemSBrowserAndNoPageIsFetchedByITerminal() => throw new PendingStepException();
}
