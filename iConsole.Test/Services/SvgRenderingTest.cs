using System.Text;
using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Common;
using SkiaSharp;

namespace iConsole.Test.Services;

[TestClass]
public class SvgRenderingTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void RenderSvg_DrawsTheShapesOverAWhiteBackground()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="5" height="10" fill="#ff0000"/></svg>""";

        using SKBitmap? bitmap = FileToTextConverterService.RenderSvg(Encoding.UTF8.GetBytes(svg));

        Assert.IsNotNull(bitmap);
        Assert.AreEqual(new SKColor(255, 0, 0), bitmap.GetPixel(bitmap.Width / 4, bitmap.Height / 2), "the left half is the red rectangle");
        Assert.AreEqual(new SKColor(255, 255, 255), bitmap.GetPixel(bitmap.Width * 3 / 4, bitmap.Height / 2), "the right half is the background");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RenderSvg_ADocumentThatIsNotSvgGivesNull()
    {
        Assert.IsNull(FileToTextConverterService.RenderSvg(Encoding.UTF8.GetBytes("not an svg document")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheViewerDrawsTheExampleAsTextArt()
    {
        var textModel = new TextModel();

        await new FileToTextConverterService().ReadFileAsync(new Url(SampleFiles.Path("example.svg")), new LocalFileSystemAdapter(), textModel);

        List<string> lines = textModel.Lines.ToList();
        Assert.IsTrue(lines.Count > 5, "the drawing is several rows of text art");
        Assert.IsFalse(lines.Any(l => l.Contains("<svg")), "the markup is not shown");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASvgThatCannotBeDrawnIsShownAsItsText()
    {
        string path = Path.Combine(Path.GetTempPath(), $"svg_{Guid.NewGuid():N}.svg");
        File.WriteAllText(path, "<svg broken");
        try
        {
            var textModel = new TextModel();

            await new FileToTextConverterService().ReadFileAsync(new Url(path), new LocalFileSystemAdapter(), textModel);

            Assert.IsTrue(textModel.Lines.Any(l => l.Contains("<svg broken")));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
