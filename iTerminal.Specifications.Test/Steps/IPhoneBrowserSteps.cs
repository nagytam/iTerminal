// Step bindings for Features/IPhoneBrowser.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class IPhoneBrowserSteps
{
    [Given(@"an iPhone is connected and trusted")]
    public void GivenAnIPhoneIsConnectedAndTrusted() => throw new PendingStepException();

    [Then(@"the iPhone is listed as {string}")]
    public void ThenTheIPhoneIsListedAsValue(string value1) => throw new PendingStepException();

    [Given(@"an iPhone is connected but not trusted")]
    public void GivenAnIPhoneIsConnectedButNotTrusted() => throw new PendingStepException();

    [Then(@"the message asks the user to trust the computer on the phone")]
    public void ThenTheMessageAsksTheUserToTrustTheComputerOnThePhone() => throw new PendingStepException();

    [Given(@"the left panel shows {string} with a photo {string}")]
    public void GivenTheLeftPanelShowsValueWithAPhotoValue(string value1, string value2) => throw new PendingStepException();
}
