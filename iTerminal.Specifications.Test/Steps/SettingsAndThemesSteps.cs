// Step bindings for Features/SettingsAndThemes.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class SettingsAndThemesSteps
{
    [Then(@"the settings dialog shows its pages in a vertical tab list")]
    public void ThenTheSettingsDialogShowsItsPagesInAVerticalTabList() => throw new PendingStepException();

    [When(@"I press {string} and change a setting")]
    public void WhenIPressValueAndChangeASetting(string value1) => throw new PendingStepException();

    [Then(@"the setting is kept when the dialog is closed and reopened")]
    public void ThenTheSettingIsKeptWhenTheDialogIsClosedAndReopened() => throw new PendingStepException();

    [When(@"I choose the theme {string}")]
    public void WhenIChooseTheThemeValue(string value1) => throw new PendingStepException();

    [Then(@"the screen is drawn in the {string} theme")]
    public void ThenTheScreenIsDrawnInTheValueTheme(string value1) => throw new PendingStepException();

    [Given(@"the theme {string} is applied")]
    public void GivenTheThemeValueIsApplied(string value1) => throw new PendingStepException();

    [Then(@"the menus and the footer are shown in German")]
    public void ThenTheMenusAndTheFooterAreShownInGerman() => throw new PendingStepException();

    [Given(@"the language is {string}")]
    public void GivenTheLanguageIsValue(string value1) => throw new PendingStepException();

    [Then(@"the menus are shown in Hungarian")]
    public void ThenTheMenusAreShownInHungarian() => throw new PendingStepException();

    [Given(@"the language is {string} and a text has no French translation")]
    public void GivenTheLanguageIsValueAndATextHasNoFrenchTranslation(string value1) => throw new PendingStepException();

    [Then(@"that text is shown in English")]
    public void ThenThatTextIsShownInEnglish() => throw new PendingStepException();

    [Then(@"{string}, {string}, {string}, {string} and {string} are listed by their own names")]
    public void ThenValueValueValueValueAndValueAreListedByTheirOwnNames(string value1, string value2, string value3, string value4, string value5) => throw new PendingStepException();
}
