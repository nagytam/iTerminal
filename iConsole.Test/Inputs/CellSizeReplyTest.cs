using iConsole.Inputs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Inputs;

[TestClass]
public class CellSizeReplyTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TheCellSizeReply_IsReportedAndNotTakenForKeys()
    {
        var decoder = new TerminalInputUtf8Decoder();
        (int Width, int Height)? size = null;
        decoder.CellSizeReported += (w, h) => size = (w, h);
        int keys = 0;

        // The reply is "ESC [ 6 ; height ; width t".
        decoder.Append(System.Text.Encoding.ASCII.GetBytes("\x1b[6;20;10t"));
        decoder.Drain(_ => keys++, _ => { });

        Assert.AreEqual((10, 20), size);
        Assert.AreEqual(0, keys);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AKeyAfterTheReply_IsStillDecoded()
    {
        var decoder = new TerminalInputUtf8Decoder();
        char? typed = null;

        decoder.Append(System.Text.Encoding.ASCII.GetBytes("\x1b[6;20;10tq"));
        decoder.Drain(k => typed = k.KeyChar, _ => { });

        Assert.AreEqual('q', typed);
    }
}
