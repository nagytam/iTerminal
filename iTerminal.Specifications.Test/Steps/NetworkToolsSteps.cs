// Step bindings for Features/NetworkTools.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class NetworkToolsSteps
{
    [Then(@"the network manager lists each interface with its address and state")]
    public void ThenTheNetworkManagerListsEachInterfaceWithItsAddressAndState() => throw new PendingStepException();

    [Given(@"the network manager is open")]
    public void GivenTheNetworkManagerIsOpen() => throw new PendingStepException();

    [Then(@"the list is read again")]
    public void ThenTheListIsReadAgain() => throw new PendingStepException();

    [Then(@"the network browser lists the computers it finds")]
    public void ThenTheNetworkBrowserListsTheComputersItFinds() => throw new PendingStepException();

    [Given(@"the network browser lists {string}")]
    public void GivenTheNetworkBrowserListsValue(string value1) => throw new PendingStepException();

    [Then(@"the focused panel lists the shares of {string}")]
    public void ThenTheFocusedPanelListsTheSharesOfValue(string value1) => throw new PendingStepException();

    [Given(@"the network browser lists a computer that does not respond")]
    public void GivenTheNetworkBrowserListsAComputerThatDoesNotRespond() => throw new PendingStepException();

    [Then(@"that computer is shown as unreachable")]
    public void ThenThatComputerIsShownAsUnreachable() => throw new PendingStepException();
}
