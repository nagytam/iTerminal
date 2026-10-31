using iConsole.Data;
using iTerminal.Core.Dialogs.Benchmark;

namespace iTerminal.Core.Commands;

public class PerformanceBenchmarkDialogCommand(PerformanceBenchmarkDialog performanceBenchmarkDialog) : ShowDialogCommand<PerformanceBenchmarkDialog>(performanceBenchmarkDialog)
{
    public override string Title => "Performance benchmark";
    public override string Description => "Score this machine's CPU, memory, disk, network and timer latency, with an overall score. Only reads; never writes to disk.";

    // Ctrl+Shift+B. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.B, true, false, true);
}
