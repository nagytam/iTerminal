using System.Text;
using iConsole.Qr;
using ZXing;
using ZXing.Common;

namespace iConsole.Test.Qr;

/// <summary>
/// Encodes with <see cref="QrEncoder"/>, draws the matrix as a greyscale image, and decodes it with ZXing.
/// ZXing is a test-only reference decoder: the product has no QR reader.
/// </summary>
[TestClass]
public class QrRoundTripTest
{
    private const int Scale = 4;

    [TestMethod]
    public void RoundTrip_ShortAsciiText_DecodesToTheInput()
    {
        AssertRoundTrip(Encoding.ASCII.GetBytes("HELLO WORLD"));
    }

    [TestMethod]
    public void RoundTrip_UrlWithA22CharacterToken_DecodesToTheInput()
    {
        AssertRoundTrip(Encoding.ASCII.GetBytes("https://192.168.1.20:8080/AbCdEf0123456789xyzQ_-"));
    }

    [TestMethod]
    public void RoundTrip_MaximumVersion10Input_DecodesToTheInput()
    {
        byte[] data = new byte[QrEncoder.MaxBytes];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)('a' + i % 26);
        }

        AssertRoundTrip(data);
    }

    [TestMethod]
    public void RoundTrip_Utf8Text_DecodesToTheSameText()
    {
        AssertRoundTrip(Encoding.UTF8.GetBytes("Grüße 日本語 ✓"));
    }

    private static void AssertRoundTrip(byte[] data)
    {
        bool[,] modules = QrEncoder.Encode(data);
        Result? result = Decode(modules);

        Assert.IsNotNull(result, "ZXing found no QR code in the rendered matrix");
        Assert.AreEqual(Encoding.UTF8.GetString(data), result.Text);
    }

    private static Result? Decode(bool[,] modules)
    {
        int size = modules.GetLength(0);
        int quiet = QrTextRenderer.DefaultQuietZone;
        int side = (size + 2 * quiet) * Scale;
        byte[] pixels = new byte[side * side];
        for (int py = 0; py < side; py++)
        {
            for (int px = 0; px < side; px++)
            {
                int mx = px / Scale - quiet;
                int my = py / Scale - quiet;
                bool dark = mx >= 0 && my >= 0 && mx < size && my < size && modules[mx, my];
                pixels[py * side + px] = dark ? (byte)0 : (byte)255;
            }
        }

        LuminanceSource source = new RGBLuminanceSource(pixels, side, side, RGBLuminanceSource.BitmapFormat.Gray8);
        BarcodeReaderGeneric reader = new()
        {
            AutoRotate = false,
            Options = new DecodingOptions { PossibleFormats = [BarcodeFormat.QR_CODE], TryHarder = true },
        };
        return reader.Decode(source);
    }
}
