using iTerminal.Core.Model.Remote;

namespace iTerminal.Core.Test.Model.Remote;

[TestClass]
public class RemoteConnectionTest
{
    private static RemoteConnection Sound() => new() { Name = "web", Host = "example.com", Username = "alice" };

    [Timeout(60_000)]
    [TestMethod]
    public void ASoundConnectionHasNoProblem()
    {
        Assert.IsNull(Sound().Validate());
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("")]
    [DataRow("-oProxyCommand=evil")]
    [DataRow("host name")]
    [DataRow("host;rm")]
    [DataRow("host'")]
    public void AHostThatIsNotAHostIsRefused(string host)
    {
        var connection = Sound();
        connection.Host = host;

        Assert.IsNotNull(connection.Validate());
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("example.com")]
    [DataRow("10.0.0.5")]
    [DataRow("srv-01")]
    [DataRow("::1")]
    [DataRow("fe80::1ff:fe23:4567:890a")]
    public void HostNamesAndAddressesAreAccepted(string host)
    {
        var connection = Sound();
        connection.Host = host;

        Assert.IsNull(connection.Validate());
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("-x")]
    [DataRow("a b")]
    [DataRow("a'b")]
    [DataRow("a;b")]
    public void AUserNameWithSyntaxIsRefused(string user)
    {
        var connection = Sound();
        connection.Username = user;

        Assert.IsNotNull(connection.Validate());
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("DOMAIN\\alice")]
    [DataRow("alice@corp.example")]
    [DataRow("")]
    public void DomainUserNamesAreAccepted(string user)
    {
        var connection = Sound();
        connection.Username = user;

        Assert.IsNull(connection.Validate());
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(0)]
    [DataRow(65536)]
    public void APortOutsideTheRangeIsRefused(int port)
    {
        var connection = Sound();
        connection.Port = port;

        Assert.IsNotNull(connection.Validate());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANameIsRequired()
    {
        var connection = Sound();
        connection.Name = "  ";

        Assert.IsNotNull(connection.Validate());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDefaultPortFollowsTheTransport()
    {
        Assert.AreEqual(22, Sound().EffectivePort);
        Assert.AreEqual(5985, new RemoteConnection { Protocol = RemoteProtocol.PowerShell, Transport = PowerShellTransport.WinRm }.EffectivePort);
        Assert.AreEqual(22, new RemoteConnection { Protocol = RemoteProtocol.PowerShell, Transport = PowerShellTransport.Ssh }.EffectivePort);
    }
}
