using iTerminal.Core.Model.Dotnet;

namespace iTerminal.Core.Test.Model.Dotnet;

[TestClass]
public class DotnetSdkTest
{
    private static string Native(string unixPath) => unixPath.Replace('/', Path.DirectorySeparatorChar);

    [Timeout(60_000)]
    [TestMethod]
    public void ParseListing_ReadsEachSdkAndItsInstallRoot_NewestFirst()
    {
        string[] lines =
        [
            "9.0.300 [/usr/lib/dotnet/sdk]",
            "10.0.113 [/usr/lib/dotnet/sdk]",
            "10.0.200-preview.1.25001.1 [/home/me/.dotnet/sdk]",
            "not a sdk line",
            "",
        ];

        List<DotnetSdk> sdks = [.. DotnetSdk.ParseListing(lines)];

        CollectionAssert.AreEqual(
            new[] { "10.0.200-preview.1.25001.1", "10.0.113", "9.0.300" },
            sdks.Select(s => s.Version).ToList());
        // The root is the folder above sdk/, written the way this platform writes paths.
        Assert.AreEqual(Native("/home/me/.dotnet"), sdks[0].Root);
        Assert.AreEqual(Native("/usr/lib/dotnet"), sdks[1].Root);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Default_IsTheNewestStableSdk_EvenWhenAPreviewIsNewer()
    {
        List<DotnetSdk> sdks =
        [
            new("10.0.200-preview.1", "/a"),
            new("10.0.113", "/b"),
            new("9.0.300", "/c"),
        ];

        Assert.AreEqual("10.0.113", DotnetSdk.Default(sdks)!.Version);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Default_OnlyPreviewsInstalled_IsTheNewestPreview()
    {
        List<DotnetSdk> sdks = [new("10.0.200-preview.1", "/a"), new("10.0.200-rc.1", "/b")];

        Assert.AreEqual("10.0.200-rc.1", DotnetSdk.Default(sdks)!.Version);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Default_NoSdks_IsNull()
    {
        Assert.IsNull(DotnetSdk.Default([]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HostPath_IsTheDotnetHostOfTheInstall()
    {
        var sdk = new DotnetSdk("10.0.113", Path.Combine("opt", "dotnet"));

        Assert.AreEqual(Path.Combine("opt", "dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), sdk.HostPath);
    }
}
