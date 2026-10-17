using iTerminal.Core.Services.Defender;

namespace iTerminal.Core.Test.Services.Defender;

[TestClass]
public class DefenderScanServiceTest
{
    [TestMethod]
    public void Interpret_NoThreats_IsClean()
    {
        var result = DefenderScanService.Interpret(0, "Scan starting...\r\nScan finished.\r\nScanning C:\\a.txt found no threats.", string.Empty);

        Assert.AreEqual(ScanOutcome.Clean, result.Outcome);
    }

    [TestMethod]
    public void Interpret_ThreatsReported_IsThreatsFound()
    {
        var result = DefenderScanService.Interpret(2, "Scanning C:\\a.txt found 1 threats.", string.Empty);

        Assert.AreEqual(ScanOutcome.ThreatsFound, result.Outcome);
    }

    [TestMethod]
    public void Interpret_NonZeroExitWithoutVerdict_IsFailed()
    {
        var result = DefenderScanService.Interpret(2, "Scan starting...", "The service has not been started.");

        Assert.AreEqual(ScanOutcome.Failed, result.Outcome);
        Assert.AreEqual("Scan starting...", result.Message);
    }

    [TestMethod]
    public async Task ScanAsync_WithoutScanner_Fails()
    {
        var service = new DefenderScanService(() => null);

        Assert.IsFalse(service.IsAvailable);
        Assert.AreEqual(ScanOutcome.Failed, (await service.ScanAsync("C:\\a.txt")).Outcome);
    }
}
