using iConsole.Services.Disassembly;

namespace iConsole.Test.Services.Disassembly;

[TestClass]
public class ExecutableDisassemblyTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void AManagedAssemblyOnDisk_IsDisassembledIntoAListing()
    {
        // The test assembly is a real .NET (PE) file, so the IL disassembler has methods to list.
        string path = typeof(ExecutableDisassemblyTest).Assembly.Location;

        IReadOnlyList<string> lines = ExecutableDisassembly.Build(path, CancellationToken.None);

        Assert.IsTrue(lines.Count > 0);
        Assert.IsTrue(lines.Any(l => l.Contains("ExecutableDisassemblyTest")), "the listing names the methods it lists");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ACancelledToken_StopsTheBuild()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            ExecutableDisassembly.Build(typeof(ExecutableDisassemblyTest).Assembly.Location, source.Token));
    }
}
