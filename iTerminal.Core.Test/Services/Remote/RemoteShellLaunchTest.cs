using iTerminal.Core.Model.Remote;
using iTerminal.Core.Services.Remote;

namespace iTerminal.Core.Test.Services.Remote;

[TestClass]
public class RemoteShellLaunchTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void SshIsStartedWithPortKeyAndDestination()
    {
        var launch = RemoteShellLaunch.Ssh(new RemoteConnection
        {
            Name = "web", Host = "example.com", Username = "alice", Port = 2222, PrivateKeyPath = "/keys/id",
        });

        Assert.AreEqual("ssh", launch.FileName);
        CollectionAssert.AreEqual(new[] { "-p", "2222", "-i", "/keys/id", "alice@example.com" }, launch.Arguments.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SshWithoutAUserOrPortIsJustTheHost()
    {
        var launch = RemoteShellLaunch.Ssh(new RemoteConnection { Name = "web", Host = "example.com" });

        CollectionAssert.AreEqual(new[] { "example.com" }, launch.Arguments.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerShellOverSshUsesHostNameUserAndKey()
    {
        var command = RemoteShellLaunch.EnterSessionCommand(new RemoteConnection
        {
            Protocol = RemoteProtocol.PowerShell, Transport = PowerShellTransport.Ssh,
            Host = "example.com", Username = "alice", Port = 2222, PrivateKeyPath = "/keys/id",
        });

        Assert.AreEqual("Enter-PSSession -HostName 'example.com' -UserName 'alice' -Port 2222 -KeyFilePath '/keys/id'", command);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerShellOverWinRmAsksForTheCredential()
    {
        var command = RemoteShellLaunch.EnterSessionCommand(new RemoteConnection
        {
            Protocol = RemoteProtocol.PowerShell, Transport = PowerShellTransport.WinRm,
            Host = "srv01", Username = "CORP\\alice",
        });

        Assert.AreEqual("Enter-PSSession -ComputerName 'srv01' -Credential 'CORP\\alice'", command);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AQuoteInAPowerShellValueIsDoubled()
    {
        Assert.AreEqual("'it''s'", RemoteShellLaunch.QuotePowerShell("it's"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerShellOverSshNeedsPowerShellSeven()
    {
        var connection = new RemoteConnection { Name = "x", Host = "h", Protocol = RemoteProtocol.PowerShell, Transport = PowerShellTransport.Ssh };

        Assert.IsFalse(RemoteShellLaunch.TryCreatePowerShell(connection, null, out var launch, out var error));
        Assert.IsNull(launch);
        StringAssert.Contains(error, "PowerShell 7");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerShellOverSshRunsInPwshAndEndsWithTheSession()
    {
        var connection = new RemoteConnection { Name = "x", Host = "h", Protocol = RemoteProtocol.PowerShell, Transport = PowerShellTransport.Ssh };

        Assert.IsTrue(RemoteShellLaunch.TryCreatePowerShell(connection, "/usr/bin/pwsh", out var launch, out _));
        Assert.AreEqual("/usr/bin/pwsh", launch!.FileName);
        CollectionAssert.DoesNotContain(launch.Arguments.ToArray(), "-NoExit");
        Assert.AreEqual("Enter-PSSession -HostName 'h'", launch.Arguments[^1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnsoundConnectionHasNoLaunch()
    {
        Assert.IsFalse(RemoteShellLaunch.TryCreate(new RemoteConnection { Name = "x", Host = "-bad" }, out var launch, out var error));
        Assert.IsNull(launch);
        Assert.IsNotNull(error);
    }
}
