using iConsole.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Data;

/// <summary>
/// The attributes a Url keeps from the enumerated <see cref="FileSystemInfo"/>, for the Long listing. A Url made
/// from a path string keeps none, and the listing shows blank for it.
/// </summary>
[TestClass]
public class UrlListingAttributesTest
{
    [TestMethod]
    public void FromEnumeratedFileSystemInfo_CapturesTheAttributes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"listing-attr-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "x");
        try
        {
            var url = new Url(new FileInfo(path));

            Assert.IsNotNull(url.Attributes, "the attributes come from the FileSystemInfo that enumeration returned");
            if (OperatingSystem.IsWindows())
            {
                Assert.IsNull(url.UnixMode, "Unix permission bits are read only on non-Windows systems");
            }
            else
            {
                Assert.IsNotNull(url.UnixMode, "the Unix permission bits come from the same FileSystemInfo");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void FromPathString_KeepsNoAttributes()
    {
        var url = new Url(Path.GetTempPath());

        Assert.IsNull(url.Attributes);
        Assert.IsNull(url.UnixMode);
    }
}
