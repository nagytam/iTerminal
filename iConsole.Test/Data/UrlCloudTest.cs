using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class UrlCloudTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void S3Url_ParsesAccessKeyBucketAndKey()
    {
        var url = new Url("s3://AKIAEXAMPLE@photos/2026/summer/a.jpg");

        Assert.AreEqual(ContainerType.S3, url.ContainerType);
        Assert.AreEqual("AKIAEXAMPLE", url.Username);
        Assert.AreEqual("photos", url.Host);
        Assert.AreEqual("/2026/summer/a.jpg", url.Path);
        Assert.AreEqual(PathType.File, url.PathType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void S3Url_WithoutABucket_IsTheBucketList()
    {
        var url = new Url("s3://AKIAEXAMPLE@/");

        Assert.AreEqual(ContainerType.S3, url.ContainerType);
        Assert.AreEqual("AKIAEXAMPLE", url.Username);
        Assert.AreEqual(string.Empty, url.Host);
        Assert.AreEqual(PathType.Folder, url.PathType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OneDriveAndGoogleDriveUrls_ParseAccountAndPath()
    {
        var one = new Url("onedrive://work/Documents/Plans/");
        var google = new Url("gdrive://me/Shared/report.pdf");

        Assert.AreEqual(ContainerType.OneDrive, one.ContainerType);
        Assert.AreEqual("work", one.Host);
        Assert.AreEqual("/Documents/Plans/", one.Path);
        Assert.AreEqual(PathType.Folder, one.PathType);
        Assert.AreEqual(ContainerType.GoogleDrive, google.ContainerType);
        Assert.AreEqual("me", google.Host);
        Assert.AreEqual("report.pdf", google.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParentAndRoot_StayOnTheDrive()
    {
        var url = new Url("s3://AKIAEXAMPLE@photos/2026/summer/");

        Assert.AreEqual("s3://AKIAEXAMPLE@photos/2026/", url.ParentUrl.FullUrl);
        Assert.AreEqual("s3://AKIAEXAMPLE@photos/", url.RootUrl.FullUrl);
        Assert.AreEqual("gdrive://me/", new Url("gdrive://me/a/b/").RootUrl.FullUrl);
    }
}
