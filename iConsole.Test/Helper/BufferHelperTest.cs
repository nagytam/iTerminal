using iConsole.Buffers;
using iConsole.Display;
using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class BufferHelperTest
{
    private static BufferLayer CreateLayer(int width, int height)
    {
        var size = new iConsole.Data.Size(width, height);
        return new BufferLayer(0, size, Color.White, Color.Black);
    }

    [TestMethod]
    public void DumpBufferToHtml_CreatesFile()
    {
        var layer = CreateLayer(5, 2);
        var tempFile = Path.GetTempFileName();
        try
        {
            BufferHelper.DumpBufferToHtml(layer, tempFile);
            Assert.IsTrue(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void DumpBufferToHtml_ContainsHtmlStructure()
    {
        var layer = CreateLayer(3, 2);
        var tempFile = Path.GetTempFileName();
        try
        {
            BufferHelper.DumpBufferToHtml(layer, tempFile);
            var content = File.ReadAllText(tempFile);
            StringAssert.Contains(content, "<html>");
            StringAssert.Contains(content, "</html>");
            StringAssert.Contains(content, "<pre>");
            StringAssert.Contains(content, "</pre>");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void DumpBufferToHtml_ContainsSpanElements()
    {
        var layer = CreateLayer(4, 1);
        var tempFile = Path.GetTempFileName();
        try
        {
            BufferHelper.DumpBufferToHtml(layer, tempFile);
            var content = File.ReadAllText(tempFile);
            StringAssert.Contains(content, "<span");
            StringAssert.Contains(content, "</span>");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void DumpBufferToHtml_WithNullLayer_WritesEmptyBody()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            BufferHelper.DumpBufferToHtml(null, tempFile);
            var content = File.ReadAllText(tempFile);
            StringAssert.Contains(content, "<html>");
            Assert.IsFalse(content.Contains("<span"), "No spans expected for null layer");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
