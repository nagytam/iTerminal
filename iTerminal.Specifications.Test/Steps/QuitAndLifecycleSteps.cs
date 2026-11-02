// Step bindings for Features/QuitAndLifecycle.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class QuitAndLifecycleSteps
{
    [Then(@"the terminal shows the cursor and the normal screen again")]
    public void ThenTheTerminalShowsTheCursorAndTheNormalScreenAgain() => throw new PendingStepException();
}
