using iConsole.Test.Common;
using iConsole.Test.Services;
using iConsole.Wasm;

namespace iConsole.Test.Wasm;

[TestClass]
public class WasmModuleSummaryTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Summarize_ListsTheSectionsImportsExportsAndCounts()
    {
        var lines = WasmModuleSummary.Summarize(SampleFiles.Bytes("example.wasm"));

        CollectionAssert.Contains(lines.ToList(), "Version: 1");
        CollectionAssert.Contains(lines.ToList(), "Sections: 7");
        CollectionAssert.Contains(lines.ToList(), "Types: 2");
        CollectionAssert.Contains(lines.ToList(), "Imports: 1");
        CollectionAssert.Contains(lines.ToList(), "  env.log (function)");
        CollectionAssert.Contains(lines.ToList(), "Functions (defined plus imported): 2");
        CollectionAssert.Contains(lines.ToList(), "Memories: 1");
        CollectionAssert.Contains(lines.ToList(), "Exports: 2");
        CollectionAssert.Contains(lines.ToList(), "  memory (memory)");
        CollectionAssert.Contains(lines.ToList(), "  answer (function)");
        CollectionAssert.Contains(lines.ToList(), "Start function: none");
        CollectionAssert.Contains(lines.ToList(), "Custom sections: producers");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Summarize_ReadsAStartFunctionWhenTheModuleDeclaresOne()
    {
        // The same module with a start section (id 8) pointing at function 1, placed before the custom section.
        byte[] module = SampleFiles.Bytes("example.wasm");
        byte[] withStart = [.. module[..^16], 0x08, 0x01, 0x01, .. module[^16..]];

        CollectionAssert.Contains(WasmModuleSummary.Summarize(withStart).ToList(), "Start function: 1");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Summarize_RefusesBytesThatAreNotAModule()
    {
        Assert.ThrowsExactly<FormatException>(() => WasmModuleSummary.Summarize("not a module at all"u8.ToArray()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Summarize_RefusesAModuleCutInsideASection()
    {
        byte[] module = SampleFiles.Bytes("example.wasm");

        Assert.ThrowsExactly<FormatException>(() => WasmModuleSummary.Summarize(module[..30]));
    }
}
