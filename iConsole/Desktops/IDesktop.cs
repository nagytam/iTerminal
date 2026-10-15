using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Diagnostics;
using iConsole.Helper;
using iConsole.Jobs;
using iConsole.Screens;

namespace iConsole.Desktops;

public interface IDesktop
{
    IRenderingDumpHelper Dump { get; }
    IScreenLogger ScreenLogger { get; }
    CancellationTokenSource CancellationTokenSource { get; }
    ConcurrentList<IScreen> Screens { get; }
    IBuffer Buffer { get; }
    ConcurrentList<IJob> Jobs { get; }
    IControl? GetModalChildControl();
    void OnIdle();
}