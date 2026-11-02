// Step bindings for Features/Benchmark.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class BenchmarkSteps
{
    [Then(@"the dialog shows a score for CPU, memory, disk, network and timer latency")]
    public void ThenTheDialogShowsAScoreForCPUMemoryDiskNetworkAndTimerLatency() => throw new PendingStepException();

    [Then(@"an overall score is shown when the run ends")]
    public void ThenAnOverallScoreIsShownWhenTheRunEnds() => throw new PendingStepException();

    [When(@"the benchmark is running")]
    public void WhenTheBenchmarkIsRunning() => throw new PendingStepException();

    [Then(@"the dialog redraws while the probes run")]
    public void ThenTheDialogRedrawsWhileTheProbesRun() => throw new PendingStepException();

    [Then(@"the run stops and the scores measured so far are shown")]
    public void ThenTheRunStopsAndTheScoresMeasuredSoFarAreShown() => throw new PendingStepException();

    [When(@"the benchmark runs")]
    public void WhenTheBenchmarkRuns() => throw new PendingStepException();

    [Then(@"no file is created, renamed or changed by the disk probe")]
    public void ThenNoFileIsCreatedRenamedOrChangedByTheDiskProbe() => throw new PendingStepException();

    [Given(@"the network probe cannot reach its server")]
    public void GivenTheNetworkProbeCannotReachItsServer() => throw new PendingStepException();

    [Then(@"the network score is shown as unavailable and the other scores are shown")]
    public void ThenTheNetworkScoreIsShownAsUnavailableAndTheOtherScoresAreShown() => throw new PendingStepException();
}
