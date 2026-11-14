using iTerminal.Core.Services.Scanning;
using iTerminal.Core.Services.Scanning.Sane;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Scanning;

[TestClass]
public class SaneScannerBackendTest
{
    /// <summary>What <c>scanimage -L</c> printed on a machine with one flatbed and one virtual test device.</summary>
    private const string Listing =
        "device 'epson2:libusb:001:004' is a Epson GT-S600 flatbed scanner\n" +
        "device 'test:0' is a Noname test virtual device\n";

    private static readonly ScannerDevice Flatbed = new("epson2:libusb:001:004", "Epson GT-S600 flatbed scanner");

    [TestMethod]
    public void ParseListing_ReadsEveryDeviceIdAndItsDescription()
    {
        IReadOnlyList<ScannerDevice> devices = SaneScannerBackend.ParseListing(Listing);

        Assert.HasCount(2, devices);
        Assert.AreEqual("epson2:libusb:001:004", devices[0].Id);
        Assert.AreEqual("Epson GT-S600 flatbed scanner", devices[0].Name);
        Assert.AreEqual("test:0", devices[1].Id);
    }

    [TestMethod]
    public void ParseListing_OfNoScannersIsEmpty()
    {
        Assert.IsEmpty(SaneScannerBackend.ParseListing("No scanners were identified. If you were expecting something different,\n"));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ListAsync_ReturnsTheDevicesTheClientPrints()
    {
        var runner = new FakeProcessRunner().Answer("scanimage -L", Listing);
        var backend = new SaneScannerBackend(runner);

        ScannerListing listing = await backend.ListAsync(CancellationToken.None);

        Assert.IsTrue(listing.IsSupported);
        Assert.IsNull(listing.Failure);
        Assert.HasCount(2, listing.Scanners);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ListAsync_SaysSaneIsMissing_WhenTheClientCannotStart()
    {
        var runner = new FakeProcessRunner().Answer("scanimage -L", string.Empty, exitCode: -1, stderr: "not found");
        var backend = new SaneScannerBackend(runner);

        ScannerListing listing = await backend.ListAsync(CancellationToken.None);

        Assert.IsEmpty(listing.Scanners);
        StringAssert.Contains(listing.Failure, "sane-utils");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ScanAsync_Flatbed_AsksForOnePngWithoutASourceName()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];
        var runner = new FakeProcessRunner().AnswerBytes("scanimage -d epson2", png);
        var backend = new SaneScannerBackend(runner);

        ScanResult result = await backend.ScanAsync(Flatbed, new ScanOptions(600, ScanColorMode.Grayscale, ScanSource.Flatbed), CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.HasCount(1, result.Pages);
        CollectionAssert.AreEqual(png, result.Pages[0].Encoded);

        string command = runner.Calls.Single().CommandLine;
        StringAssert.Contains(command, "--format=png");
        StringAssert.Contains(command, "--resolution 600");
        StringAssert.Contains(command, "--mode Gray");
        StringAssert.DoesNotMatch(command, new System.Text.RegularExpressions.Regex("--source"));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ScanAsync_Feeder_RunsABatchFromTheDocumentFeederAndSaysWhenItIsEmpty()
    {
        // scanimage exits with status 7 when a batch runs out of sheets. The fake writes no page files, so nothing is read.
        var runner = new FakeProcessRunner().AnswerBytes("scanimage -d epson2", [], exitCode: SaneScannerBackend.NoMoreDocuments);
        var backend = new SaneScannerBackend(runner);

        ScanResult result = await backend.ScanAsync(Flatbed, new ScanOptions(300, ScanColorMode.Color, ScanSource.Feeder), CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("The document feeder is empty.", result.Message);

        string command = runner.Calls.Single().CommandLine;
        StringAssert.Contains(command, "--source ADF");
        StringAssert.Contains(command, "--batch=");
        StringAssert.Contains(command, "page%03d.png");
    }

    [TestMethod]
    public void ModeName_MapsEachColourModeToTheNameSaneUses()
    {
        Assert.AreEqual("Color", SaneScannerBackend.ModeName(ScanColorMode.Color));
        Assert.AreEqual("Gray", SaneScannerBackend.ModeName(ScanColorMode.Grayscale));
        Assert.AreEqual("Lineart", SaneScannerBackend.ModeName(ScanColorMode.BlackAndWhite));
    }
}
