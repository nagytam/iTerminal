using iTerminal.Core.Services.Scanning;
using iTerminal.Core.Services.Scanning.Twain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Services.Scanning.Twain;

/// <summary>The colour mode the person picks becomes the TWAIN pixel type the scanner is set to. The native session itself needs Windows.</summary>
[TestClass]
public class TwainPixelTypeTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void EachColorModeAsksTheScannerForItsTwainPixelType()
    {
        Assert.AreEqual((ushort)0, TwainSession.PixelType(ScanColorMode.BlackAndWhite), "TWPT_BW");
        Assert.AreEqual((ushort)1, TwainSession.PixelType(ScanColorMode.Grayscale), "TWPT_GRAY");
        Assert.AreEqual((ushort)2, TwainSession.PixelType(ScanColorMode.Color), "TWPT_RGB");
    }
}
