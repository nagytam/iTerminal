// Step bindings for Features/SpecialisedViewers.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class SpecialisedViewersSteps
{
    [Then(@"the XML viewer shows the document as a tree")]
    public void ThenTheXMLViewerShowsTheDocumentAsATree() => throw new PendingStepException();

    [Given(@"the XML viewer shows a document with many {string} elements")]
    public void GivenTheXMLViewerShowsADocumentWithManyValueElements(string value1) => throw new PendingStepException();

    [When(@"I press {string} and type {string} and press {string}")]
    public void WhenIPressValueAndTypeValueAndPressValue(string value1, string value2, string value3) => throw new PendingStepException();

    [Then(@"the matching nodes are listed")]
    public void ThenTheMatchingNodesAreListed() => throw new PendingStepException();

    [Given(@"the XML viewer shows a document whose DOCTYPE names a DTD")]
    public void GivenTheXMLViewerShowsADocumentWhoseDOCTYPENamesADTD() => throw new PendingStepException();

    [Then(@"the viewer reports whether the document is valid")]
    public void ThenTheViewerReportsWhetherTheDocumentIsValid() => throw new PendingStepException();

    [Then(@"the JSON viewer shows the document as a tree")]
    public void ThenTheJSONViewerShowsTheDocumentAsATree() => throw new PendingStepException();

    [Given(@"the JSON viewer shows a file with a missing comma on line 3")]
    public void GivenTheJSONViewerShowsAFileWithAMissingCommaOnLine3() => throw new PendingStepException();

    [Then(@"the viewer reports the error on line 3")]
    public void ThenTheViewerReportsTheErrorOnLine3() => throw new PendingStepException();

    [Given(@"the JSON viewer shows {string}")]
    public void GivenTheJSONViewerShowsValue(string value1) => throw new PendingStepException();

    [Then(@"the file's text is shown")]
    public void ThenTheFileSTextIsShown() => throw new PendingStepException();

    [Then(@"the CSV viewer shows the rows as a grid")]
    public void ThenTheCSVViewerShowsTheRowsAsAGrid() => throw new PendingStepException();

    [Given(@"the CSV viewer shows {string}")]
    public void GivenTheCSVViewerShowsValue(string value1) => throw new PendingStepException();

    [When(@"I type the query {string}")]
    public void WhenITypeTheQueryValue(string value1) => throw new PendingStepException();

    [Then(@"at most five rows are shown with amounts above 100 in order")]
    public void ThenAtMostFiveRowsAreShownWithAmountsAbove100InOrder() => throw new PendingStepException();

    [Given(@"the CSV viewer shows rows")]
    public void GivenTheCSVViewerShowsRows() => throw new PendingStepException();

    [When(@"I type a query that cannot be parsed")]
    public void WhenITypeAQueryThatCannotBeParsed() => throw new PendingStepException();

    [Then(@"the rows already shown stay on screen")]
    public void ThenTheRowsAlreadyShownStayOnScreen() => throw new PendingStepException();

    [Then(@"the status line says the query is not valid")]
    public void ThenTheStatusLineSaysTheQueryIsNotValid() => throw new PendingStepException();

    [Given(@"the CSV viewer shows {string} with accents")]
    public void GivenTheCSVViewerShowsValueWithAccents(string value1) => throw new PendingStepException();

    [Then(@"the accents are shown correctly")]
    public void ThenTheAccentsAreShownCorrectly() => throw new PendingStepException();

    [Given(@"{string} is a SQLite database")]
    public void GivenValueIsASQLiteDatabase(string value1) => throw new PendingStepException();

    [Then(@"the SQLite viewer lists the tables of the database")]
    public void ThenTheSQLiteViewerListsTheTablesOfTheDatabase() => throw new PendingStepException();

    [Given(@"the SQLite viewer is open")]
    public void GivenTheSQLiteViewerIsOpen() => throw new PendingStepException();

    [Then(@"the query is refused and the database is unchanged")]
    public void ThenTheQueryIsRefusedAndTheDatabaseIsUnchanged() => throw new PendingStepException();

    [Given(@"the SQLite viewer is open on a table with ten rows")]
    public void GivenTheSQLiteViewerIsOpenOnATableWithTenRows() => throw new PendingStepException();

    [Then(@"the ten rows are shown")]
    public void ThenTheTenRowsAreShown() => throw new PendingStepException();

    [When(@"I type a query that runs for more than ten seconds")]
    public void WhenITypeAQueryThatRunsForMoreThanTenSeconds() => throw new PendingStepException();

    [Then(@"the query stops and the status line says it timed out")]
    public void ThenTheQueryStopsAndTheStatusLineSaysItTimedOut() => throw new PendingStepException();

    [Given(@"{string} is a plain text file")]
    public void GivenValueIsAPlainTextFile(string value1) => throw new PendingStepException();

    [Then(@"the text viewer opens {string}")]
    public void ThenTheTextViewerOpensValue(string value1) => throw new PendingStepException();

    [Then(@"the solution viewer lists the projects of the solution")]
    public void ThenTheSolutionViewerListsTheProjectsOfTheSolution() => throw new PendingStepException();

    [Given(@"the solution viewer is open on {string}")]
    public void GivenTheSolutionViewerIsOpenOnValue(string value1) => throw new PendingStepException();

    [Then(@"{string} runs and its output is shown")]
    public void ThenValueRunsAndItsOutputIsShown(string value1) => throw new PendingStepException();

    [Then(@"the web browser shows {string} without running scripts")]
    public void ThenTheWebBrowserShowsValueWithoutRunningScripts(string value1) => throw new PendingStepException();

    [Then(@"the page opens in the system's default browser")]
    public void ThenThePageOpensInTheSystemSDefaultBrowser() => throw new PendingStepException();

    [Then(@"the picture is drawn as ASCII art")]
    public void ThenThePictureIsDrawnAsASCIIArt() => throw new PendingStepException();

    [Then(@"the camera, date and dimensions of the picture are shown")]
    public void ThenTheCameraDateAndDimensionsOfThePictureAreShown() => throw new PendingStepException();

    [Given(@"{string} is under the cursor and ffmpeg is installed")]
    public void GivenValueIsUnderTheCursorAndFfmpegIsInstalled(string value1) => throw new PendingStepException();

    [Then(@"the video viewer opens {string}")]
    public void ThenTheVideoViewerOpensValue(string value1) => throw new PendingStepException();

    [Given(@"the video viewer is playing {string}")]
    public void GivenTheVideoViewerIsPlayingValue(string value1) => throw new PendingStepException();

    [Then(@"the video is paused")]
    public void ThenTheVideoIsPaused() => throw new PendingStepException();

    [Then(@"the music viewer shows the artist, title and album")]
    public void ThenTheMusicViewerShowsTheArtistTitleAndAlbum() => throw new PendingStepException();

    [Given(@"the music viewer shows {string}")]
    public void GivenTheMusicViewerShowsValue(string value1) => throw new PendingStepException();

    [When(@"I press {string} and change the title to {string} and save")]
    public void WhenIPressValueAndChangeTheTitleToValueAndSave(string value1, string value2) => throw new PendingStepException();

    [Then(@"the title tag of {string} is {string}")]
    public void ThenTheTitleTagOfValueIsValue(string value1, string value2) => throw new PendingStepException();
}
