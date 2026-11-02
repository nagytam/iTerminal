// Step bindings for Features/InternetTools.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class InternetToolsSteps
{
    [When(@"I type {string} in the address bar and press {string}")]
    public void WhenITypeValueInTheAddressBarAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the page is shown as text with its links")]
    public void ThenThePageIsShownAsTextWithItsLinks() => throw new PendingStepException();

    [Given(@"the browser shows a page with a link {string}")]
    public void GivenTheBrowserShowsAPageWithALinkValue(string value1) => throw new PendingStepException();

    [When(@"I move to {string} and press {string}")]
    public void WhenIMoveToValueAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the linked page is shown")]
    public void ThenTheLinkedPageIsShown() => throw new PendingStepException();

    [Given(@"the browser has visited two pages")]
    public void GivenTheBrowserHasVisitedTwoPages() => throw new PendingStepException();

    [Then(@"the first page is shown")]
    public void ThenTheFirstPageIsShown() => throw new PendingStepException();

    [Then(@"the second page is shown")]
    public void ThenTheSecondPageIsShown() => throw new PendingStepException();

    [When(@"I open a new tab in the browser")]
    public void WhenIOpenANewTabInTheBrowser() => throw new PendingStepException();

    [Then(@"two tabs are listed")]
    public void ThenTwoTabsAreListed() => throw new PendingStepException();

    [Given(@"the browser shows {string}")]
    public void GivenTheBrowserShowsValue(string value1) => throw new PendingStepException();

    [When(@"I add a bookmark")]
    public void WhenIAddABookmark() => throw new PendingStepException();

    [Then(@"the bookmark is listed in the bookmarks")]
    public void ThenTheBookmarkIsListedInTheBookmarks() => throw new PendingStepException();

    [Given(@"the browser shows a long page")]
    public void GivenTheBrowserShowsALongPage() => throw new PendingStepException();

    [When(@"I search the page for {string}")]
    public void WhenISearchThePageForValue(string value1) => throw new PendingStepException();

    [Then(@"the matches are highlighted")]
    public void ThenTheMatchesAreHighlighted() => throw new PendingStepException();

    [Given(@"the browser shows a form with a {string} field")]
    public void GivenTheBrowserShowsAFormWithAValueField(string value1) => throw new PendingStepException();

    [When(@"I type {string} in {string} and submit the form")]
    public void WhenITypeValueInValueAndSubmitTheForm(string value1, string value2) => throw new PendingStepException();

    [Then(@"the form is sent with {string} set to {string}")]
    public void ThenTheFormIsSentWithValueSetToValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"the browser shows a page")]
    public void GivenTheBrowserShowsAPage() => throw new PendingStepException();

    [Then(@"the HTML source of the page is shown")]
    public void ThenTheHTMLSourceOfThePageIsShown() => throw new PendingStepException();

    [Then(@"the page is saved to a file")]
    public void ThenThePageIsSavedToAFile() => throw new PendingStepException();

    [Given(@"the browser shows a page with a script that writes text")]
    public void GivenTheBrowserShowsAPageWithAScriptThatWritesText() => throw new PendingStepException();

    [Then(@"the text written by the script does not appear")]
    public void ThenTheTextWrittenByTheScriptDoesNotAppear() => throw new PendingStepException();

    [Then(@"the mail client is shown")]
    public void ThenTheMailClientIsShown() => throw new PendingStepException();

    [Then(@"the map is shown with a cursor position")]
    public void ThenTheMapIsShownWithACursorPosition() => throw new PendingStepException();
}
