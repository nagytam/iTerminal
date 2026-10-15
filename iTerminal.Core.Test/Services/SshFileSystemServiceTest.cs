using iTerminal.Core.Services;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class SshFileSystemServiceTest
{
    [TestMethod]
    [Ignore("Requires network connectivity and actual SSH server")]
    [Timeout(5000)]
    public void SshFileSystem()
    {
        var mockLogger = new Mock<ILogger>();
        var sshFileSystem = new SshFileSystemService(mockLogger.Object);
        sshFileSystem.Connect("nagy.ovh", "tamas", null, @"c:\users\tamas\.ssh\id_rsa");
    }
}
