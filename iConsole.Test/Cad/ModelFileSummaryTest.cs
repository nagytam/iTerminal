using System.Buffers.Binary;
using System.Text;
using iConsole.Cad;

namespace iConsole.Test.Cad;

/// <summary>
/// The DXF, OBJ and STL summaries, checked on the examples in Test/FileTypes (linked into this project) and on
/// synthetic bytes for the layouts the examples do not cover.
/// </summary>
[TestClass]
public class ModelFileSummaryTest
{
    private static byte[] Sample(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "FileTypes", name));

    [TestMethod]
    public void DxfExample_ListsTheVersionExtentsLayersAndEntities()
    {
        var result = ModelFileSummary.Summarize("dxf", Sample("example.dxf"));

        Assert.IsTrue(result.ShowSource, "an ASCII drawing keeps its source below the summary");
        CollectionAssert.Contains(result.Lines.ToList(), "Version: AC1009");
        CollectionAssert.Contains(result.Lines.ToList(), "Extents: (0, 0) – (10, 10)");
        CollectionAssert.Contains(result.Lines.ToList(), "Layers: 2 (0, WALLS)");
        CollectionAssert.Contains(result.Lines.ToList(), "Entities: 3");
        CollectionAssert.Contains(result.Lines.ToList(), "  LINE: 2");
        CollectionAssert.Contains(result.Lines.ToList(), "  CIRCLE: 1");
    }

    [TestMethod]
    public void BinaryDxf_SaysItsEntitiesAreNotListedAndShowsNoSource()
    {
        byte[] content = [.. Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001a\0"), 0, 1, 2, 3];

        var result = ModelFileSummary.Summarize("dxf", content);

        Assert.IsFalse(result.ShowSource);
        Assert.AreEqual(2, result.Lines.Count);
        StringAssert.Contains(result.Lines[1], "binary DXF");
    }

    [TestMethod]
    public void ObjExample_CountsTheElementsAndTheBounds()
    {
        var result = ModelFileSummary.Summarize("obj", Sample("example.obj"));
        var lines = result.Lines.ToList();

        Assert.IsTrue(result.ShowSource);
        CollectionAssert.Contains(lines, "Vertices: 8");
        CollectionAssert.Contains(lines, "Texture coordinates: 4");
        CollectionAssert.Contains(lines, "Normals: 2");
        CollectionAssert.Contains(lines, "Faces: 6");
        CollectionAssert.Contains(lines, "Groups: 2");
        CollectionAssert.Contains(lines, "Objects: 1");
        CollectionAssert.Contains(lines, "Materials: red, blue");
        CollectionAssert.Contains(lines, "Material libraries: example.mtl");
        CollectionAssert.Contains(lines, "Bounds: (0, 0, 0) – (1, 1, 1)");
    }

    [TestMethod]
    public void AsciiStlExample_NamesTheSolidAndCountsItsTriangles()
    {
        var result = ModelFileSummary.Summarize("stl", Sample("example.stl"));
        var lines = result.Lines.ToList();

        Assert.IsTrue(result.ShowSource);
        CollectionAssert.Contains(lines, "Format: ASCII");
        CollectionAssert.Contains(lines, "Name: example");
        CollectionAssert.Contains(lines, "Triangles: 4");
        CollectionAssert.Contains(lines, "Bounds: (0, 0, 0) – (1, 1, 1)");
    }

    [TestMethod]
    public void BinaryStlExample_IsReadFromItsLayoutAndShowsNoSource()
    {
        var result = ModelFileSummary.Summarize("stl", Sample("example-binary.stl"));
        var lines = result.Lines.ToList();

        Assert.IsFalse(result.ShowSource, "a binary file has no text to show below the summary");
        CollectionAssert.Contains(lines, "Format: binary");
        CollectionAssert.Contains(lines, "Triangles: 4");
        CollectionAssert.Contains(lines, "Bounds: (0, 0, 0) – (1, 1, 1)");
    }

    [TestMethod]
    public void BinaryStlWhoseHeaderStartsWithSolid_IsStillBinary()
    {
        // Many binary exporters write "solid" in the header. The triangle count and length decide, not the word.
        byte[] content = Sample("example-binary.stl");
        Encoding.ASCII.GetBytes("solid").CopyTo(content, 0);

        var result = ModelFileSummary.Summarize("stl", content);

        CollectionAssert.Contains(result.Lines.ToList(), "Format: binary");
    }

    [TestMethod]
    public void BinaryStlWithTruncatedData_CountsOnlyTheTrianglesPresent()
    {
        byte[] full = Sample("example-binary.stl");
        byte[] truncated = full[..(84 + 2 * 50 + 10)];

        var result = ModelFileSummary.Summarize("stl", truncated);

        CollectionAssert.Contains(result.Lines.ToList(), "Triangles: 2");
    }

    [TestMethod]
    [DataRow("dxf", "This is not a drawing.\nat all\n")]
    [DataRow("obj", "hello\nworld\n")]
    [DataRow("stl", "hello world")]
    public void TextThatIsNotTheFormat_IsRefusedSoTheViewerShowsItAsText(string extension, string text)
    {
        Assert.ThrowsExactly<FormatException>(() => ModelFileSummary.Summarize(extension, Encoding.ASCII.GetBytes(text)));
    }

    [TestMethod]
    public void Extensions_CoverTheThreeFormats()
    {
        CollectionAssert.AreEquivalent(new[] { "dxf", "obj", "stl" }, ModelFileSummary.Extensions.ToArray());
    }

    [TestMethod]
    public void BinaryStlHelper_ReadsLittleEndianFloats()
    {
        // A one-triangle file whose only vertex is (2.5, 0, 0): the bounds must read the little-endian float.
        byte[] content = new byte[84 + 50];
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(80, 4), 1);
        BinaryPrimitives.WriteSingleLittleEndian(content.AsSpan(84 + 12, 4), 2.5f);

        var result = ModelFileSummary.Summarize("stl", content);

        CollectionAssert.Contains(result.Lines.ToList(), "Bounds: (0, 0, 0) – (2.5, 0, 0)");
    }
}
