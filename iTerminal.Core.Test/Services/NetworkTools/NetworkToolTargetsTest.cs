using System.Net;
using iTerminal.Core.Services.NetworkTools;

namespace iTerminal.Core.Test.Services.NetworkTools;

[TestClass]
public class NetworkToolTargetsTest
{
    [TestMethod]
    public void AHostNameOrAddressIsTrimmed()
    {
        Assert.AreEqual("example.com", NetworkToolTargets.ParseHost("  example.com "));
        Assert.AreEqual("192.0.2.1", NetworkToolTargets.ParseHost("192.0.2.1"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("https://example.com")]
    [DataRow("example.com/path")]
    [DataRow("two words")]
    public void AHostWithASchemePathOrSpaceOrNothingIsRefused(string target)
    {
        Assert.ThrowsExactly<ArgumentException>(() => NetworkToolTargets.ParseHost(target));
    }

    [TestMethod]
    public void HostAndPortAreSplitAtTheLastColon()
    {
        (string host, int port) = NetworkToolTargets.ParseHostAndPort("example.com:443");
        Assert.AreEqual("example.com", host);
        Assert.AreEqual(443, port);
    }

    [TestMethod]
    public void AnIPv6AddressNeedsBracketsAndTheyAreRemoved()
    {
        (string host, int port) = NetworkToolTargets.ParseHostAndPort("[2001:db8::1]:8080");
        Assert.AreEqual("2001:db8::1", host);
        Assert.AreEqual(8080, port);
        Assert.ThrowsExactly<ArgumentException>(() => NetworkToolTargets.ParseHostAndPort("2001:db8::1:80"));
    }

    [TestMethod]
    [DataRow("example.com")]
    [DataRow("example.com:0")]
    [DataRow("example.com:65536")]
    [DataRow("example.com:http")]
    [DataRow(":443")]
    public void APortOutsideOneToSixtyFiveThousandFiveHundredThirtyFiveIsRefused(string target)
    {
        Assert.ThrowsExactly<ArgumentException>(() => NetworkToolTargets.ParseHostAndPort(target));
    }

    [TestMethod]
    public void AWebAddressWithoutASchemeIsHttps()
    {
        Assert.AreEqual("https://example.com/", NetworkToolTargets.ParseWebAddress("example.com").ToString());
        Assert.AreEqual("http://example.com/x", NetworkToolTargets.ParseWebAddress("http://example.com/x").ToString());
    }

    [TestMethod]
    [DataRow("ftp://example.com")]
    [DataRow("")]
    public void OnlyHttpAndHttpsAddressesAreAccepted(string target)
    {
        Assert.ThrowsExactly<ArgumentException>(() => NetworkToolTargets.ParseWebAddress(target));
    }

    [TestMethod]
    public void AnAddressForReverseDnsMustParse()
    {
        Assert.AreEqual("8.8.8.8", NetworkToolTargets.ParseIpAddress("8.8.8.8").ToString());
        Assert.ThrowsExactly<ArgumentException>(() => NetworkToolTargets.ParseIpAddress("example.com"));
    }

    [TestMethod]
    public void TheRefersLineOfAWhoisAnswerNamesTheNextServer()
    {
        const string iana = "domain:       COM\r\nrefer:        whois.verisign-grs.com\r\n";
        Assert.AreEqual("whois.verisign-grs.com", NetworkToolTargets.ParseWhoisReferral(iana));
    }

    [TestMethod]
    public void AWhoisAnswerWithoutARefersLineAnswersTheQuestionItself()
    {
        Assert.IsNull(NetworkToolTargets.ParseWhoisReferral("Domain Name: EXAMPLE.ORG\nRegistrar: X\n"));
    }

    [TestMethod]
    public void ASubnetListsItsUsableAddresses()
    {
        IReadOnlyList<IPAddress> subnet = NetworkToolTargets.ParseSubnet("192.168.1.0/24");
        Assert.AreEqual(254, subnet.Count, "a /24 has 256 addresses, less the network and broadcast ones");
        Assert.AreEqual("192.168.1.1", subnet[0].ToString());
        Assert.AreEqual("192.168.1.254", subnet[^1].ToString());
        Assert.AreEqual(2, NetworkToolTargets.ParseSubnet("10.0.0.0/31").Count, "a /31 keeps both addresses");
        Assert.AreEqual("10.0.0.7", NetworkToolTargets.ParseSubnet("10.0.0.7/32").Single().ToString(), "a /32 is its one address");
    }

    [TestMethod]
    public void ASubnetThatIsNotIPv4CIDROrTooLargeIsRefused()
    {
        foreach (string text in new[] { "192.168.1.0/21", "192.168.1.0/33", "192.168.1/24", "2001:db8::/64", "example.com", "192.168.1.0", "" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => NetworkToolTargets.ParseSubnet(text), text);
        }

        ArgumentException hostBits = Assert.ThrowsExactly<ArgumentException>(() => NetworkToolTargets.ParseSubnet("192.168.1.77/24"));
        StringAssert.Contains(hostBits.Message, "192.168.1.0/24", "the refusal names the network address");
    }
}
