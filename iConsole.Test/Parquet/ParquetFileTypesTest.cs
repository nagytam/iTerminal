using iConsole.Parquet;

namespace iConsole.Test.Parquet;

[TestClass]
public class ParquetFileTypesTest
{
    [TestMethod]
    [DataRow("data.parquet")]
    [DataRow("DATA.PARQUET")]
    [DataRow("/tmp/table.parq")]
    public void IsParquetFile_ForTheParquetEndings_IsTrue(string path)
    {
        Assert.IsTrue(ParquetFileTypes.IsParquetFile(path));
    }

    [TestMethod]
    [DataRow("data.csv")]
    [DataRow("data.parquet.txt")]
    [DataRow("parquet")]
    [DataRow("")]
    public void IsParquetFile_ForAnyOtherName_IsFalse(string path)
    {
        Assert.IsFalse(ParquetFileTypes.IsParquetFile(path));
    }

    [TestMethod]
    public void HasSignature_OnThePar1Magic_IsTrue()
    {
        Assert.IsTrue(ParquetFileTypes.HasSignature("PAR1\0\0\0\0"u8));
    }

    [TestMethod]
    public void HasSignature_OnAnEncryptedFileOrAShortOne_IsFalse()
    {
        // An encrypted Parquet file starts with PARE and is read by the generic viewer.
        Assert.IsFalse(ParquetFileTypes.HasSignature("PARE\0\0\0\0"u8));
        Assert.IsFalse(ParquetFileTypes.HasSignature("PAR"u8));
        Assert.IsFalse(ParquetFileTypes.HasSignature(ReadOnlySpan<byte>.Empty));
    }

    [TestMethod]
    public void FileHasSignature_ReadsTheMagicFromDisk()
    {
        string folder = Path.Combine(Path.GetTempPath(), "iterminal-parquet-types-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string parquet = Path.Combine(folder, "a.parquet");
            string text = Path.Combine(folder, "b.parquet");
            string empty = Path.Combine(folder, "c.parquet");
            File.WriteAllBytes(parquet, "PAR1 rest of the file"u8.ToArray());
            File.WriteAllBytes(text, "name,value\n"u8.ToArray());
            File.WriteAllBytes(empty, []);

            Assert.IsTrue(ParquetFileTypes.FileHasSignature(parquet));
            Assert.IsFalse(ParquetFileTypes.FileHasSignature(text));
            Assert.IsFalse(ParquetFileTypes.FileHasSignature(empty));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
