using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Services.Shell;
using Moq;

namespace iTerminal.Core.Test.Services.FileAssociations;

[TestClass]
public class FileLauncherUrlTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("file:///etc/passwd")]
    [DataRow("javascript:alert(1)")]
    [DataRow("calc.exe")]
    [DataRow("C:\\Windows\\System32\\cmd.exe")]
    [DataRow("")]
    public async Task OnlyWebAddressesAreOpenedAsAddresses(string address)
    {
        FileLauncher launcher = new(Mock.Of<IInteractiveShellService>());

        LaunchOutcome outcome = await launcher.OpenUrlAsync(address);

        Assert.IsFalse(outcome.Started, "anything else a shell was asked to open would start as a program");
        StringAssert.Contains(outcome.Failure ?? string.Empty, "is not a web address");
    }
}
