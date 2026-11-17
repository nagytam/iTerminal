using iTerminal.Core.Services.Archives;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Archives;

[TestClass]
public class RarArchiveWriterTest
{
    private static CompressionOptions Options(int level = 3, string? password = null, bool encryptNames = false) =>
        new(ArchiveFormat.Rar, level, password, ArchiveEncryption.Aes256, encryptNames, ReplaceExisting: true);

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(0, "-m0")]
    [DataRow(3, "-m3")]
    [DataRow(9, "-m5")]
    [DataRow(-2, "-m0")]
    public void TheLevelIsClampedToRarsFiveSteps(int level, string expected)
    {
        var arguments = RarArchiveWriter.BuildArguments("x.rar", Options(level), ["a.txt"]);

        CollectionAssert.Contains(arguments.ToList(), expected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAPasswordNoPasswordSwitchIsGiven()
    {
        var arguments = RarArchiveWriter.BuildArguments("x.rar", Options(), ["a.txt"]);

        CollectionAssert.DoesNotContain(arguments.ToList(), "-p");
        CollectionAssert.DoesNotContain(arguments.ToList(), "-hp");
        Assert.AreEqual("a", arguments[0]);
        Assert.AreEqual("--", arguments[^3]);
        Assert.AreEqual("x.rar", arguments[^2]);
        Assert.AreEqual("a.txt", arguments[^1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APasswordIsAskedFromStdinAndNeverInTheArguments()
    {
        var plain = RarArchiveWriter.BuildArguments("x.rar", Options(password: "hunter2"), ["a.txt"]);
        var headers = RarArchiveWriter.BuildArguments("x.rar", Options(password: "hunter2", encryptNames: true), ["a.txt"]);

        CollectionAssert.Contains(plain.ToList(), "-p");
        CollectionAssert.Contains(headers.ToList(), "-hp");
        CollectionAssert.DoesNotContain(plain.ToList(), "hunter2");
        CollectionAssert.DoesNotContain(headers.ToList(), "hunter2");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WriteAsync_SendsThePasswordTwiceOnStdin()
    {
        var root = Path.Combine(Path.GetTempPath(), "rar-writer-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "a.txt");
            File.WriteAllText(file, "hello");
            var runner = new FakeProcessRunner().Answer("/usr/bin/rar a", "Adding a.txt 100% OK\nDone\n");
            var writer = new RarArchiveWriter(runner, () => "/usr/bin/rar");

            await writer.WriteAsync(
                Path.Combine(root, "out.rar"),
                [new ArchiveItem("a.txt", false, 5, DateTime.Now, LocalPath: file)],
                Options(password: "hunter2"),
                null,
                CancellationToken.None);

            var call = runner.Calls.Single();
            var line = "hunter2" + Environment.NewLine;
            CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(line + line), call.StandardInput);
            CollectionAssert.DoesNotContain(call.Arguments.ToList(), "hunter2");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WriteAsync_ReportsRarsErrorLine()
    {
        var root = Path.Combine(Path.GetTempPath(), "rar-writer-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "a.txt");
            File.WriteAllText(file, "hello");
            var runner = new FakeProcessRunner().Answer("/usr/bin/rar a", "", exitCode: 3, stderr: "ERROR: Cannot create out.rar\n");
            var writer = new RarArchiveWriter(runner, () => "/usr/bin/rar");

            var error = await AssertAsync.ThrowsAsync<IOException>(() => writer.WriteAsync(
                Path.Combine(root, "out.rar"),
                [new ArchiveItem("a.txt", false, 5, DateTime.Now, LocalPath: file)],
                Options(),
                null,
                CancellationToken.None));

            Assert.AreEqual("rar: ERROR: Cannot create out.rar", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheWriterIsUnavailableWithoutTheProgram()
    {
        var writer = new RarArchiveWriter(new FakeProcessRunner(), () => null);

        Assert.IsFalse(writer.IsAvailable);
        Assert.IsNull(writer.Executable);
    }
}
