using iTerminal.Core.Helper;

namespace iTerminal.Core.Test.Helper;

/// <summary>Naming something inside a folder URL — above all at a drive's root, where trimming used to eat the scheme.</summary>
[TestClass]
public class UrlPathTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("memdrive://", "one", true, "memdrive://one/")]
    [DataRow("memdrive://x/", "one", false, "memdrive://x/one")]
    [DataRow("ssh://me@host:/", "a.txt", false, "ssh://me@host:/a.txt")]
    [DataRow("docker://web/etc/", "nginx", true, "docker://web/etc/nginx/")]
    [DataRow(@"file://C:\dst\", "a.zip", false, @"file://C:\dst\a.zip")]
    [DataRow(@"file://C:\dst", "sub/deep.txt", false, @"file://C:\dst\sub\deep.txt")]
    [DataRow("memdrive://x", @"sub\deep.txt", false, "memdrive://x/sub/deep.txt")]
    public void AChildIsNamedInsideTheFolder(string folder, string relative, bool isFolder, string expected)
    {
        Assert.AreEqual(expected, UrlPath.Child(folder, relative, isFolder));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("memdrive://", "memdrive://")]
    [DataRow("memdrive:///", "memdrive://")]
    [DataRow("memdrive://x//", "memdrive://x")]
    [DataRow(@"file://C:\a\", @"file://C:\a")]
    public void TrimmingNeverCutsIntoTheScheme(string url, string expected)
    {
        Assert.AreEqual(expected, UrlPath.TrimFolder(url));
    }
}
