using iTerminal.Core.Services.Origin;

namespace iTerminal.Core.Test.Services.Origin;

/// <summary>The download mark: the Zone.Identifier text, a real NTFS stream on Windows, and the attribute tools elsewhere.</summary>
[TestClass]
public class FileOriginServiceTest
{
    [TestMethod]
    public void Parser_ReadsZoneReferrerAndHost_WithCrLfAndBom()
    {
        FileOrigin? origin = FileOriginService.ParseZoneIdentifier("﻿[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://a.example/p\r\nHostUrl=https://a.example/f.zip\r\n");

        Assert.AreEqual(3, origin?.ZoneId);
        Assert.AreEqual("Internet", origin?.ZoneName);
        Assert.AreEqual("https://a.example/p", origin?.ReferrerUrl);
        Assert.AreEqual("https://a.example/f.zip", origin?.Source);
    }

    [TestMethod]
    public void Parser_ReadsLfOnlyText_AndTheLastDuplicateWins()
    {
        FileOrigin? origin = FileOriginService.ParseZoneIdentifier("[ZoneTransfer]\nZoneId=2\nZoneId=4\n");

        Assert.AreEqual(4, origin?.ZoneId);
        Assert.AreEqual("Restricted sites", origin?.ZoneName);
    }

    [TestMethod]
    public void Parser_IgnoresKeysOutsideTheZoneTransferSection_AndMalformedLines()
    {
        Assert.IsNull(FileOriginService.ParseZoneIdentifier("ZoneId=3\n"));
        Assert.IsNull(FileOriginService.ParseZoneIdentifier("[Other]\nZoneId=3\n"));
        Assert.IsNull(FileOriginService.ParseZoneIdentifier("[ZoneTransfer]\nZoneId=abc\n=\njunk\n"));
        Assert.IsNull(FileOriginService.ParseZoneIdentifier(string.Empty));
    }

    [TestMethod]
    public void Parser_StripsControlCharactersAndCapsLength()
    {
        FileOrigin? origin = FileOriginService.ParseZoneIdentifier("[ZoneTransfer]\nHostUrl=https://x/\u001b[31mred\u0007\nReferrerUrl=" + new string('a', 5000) + "\n");

        Assert.AreEqual("https://x/[31mred", origin?.HostUrl);
        Assert.AreEqual(2048, origin?.ReferrerUrl?.Length);
    }

    [TestMethod]
    public void UnknownZoneIsNamedByNumber()
    {
        Assert.AreEqual("Zone 7", new FileOrigin(7, null, null, null, null).ZoneName);
    }

    private static string TempFile(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), "iterm-origin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "a.zip");
        File.WriteAllText(file, "data");
        return file;
    }

    [TestMethod]
    public async Task Windows_ReadsAndRemovesTheRealStream()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string file = TempFile(out string folder);
        try
        {
            try
            {
                File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://h.example/a.zip\r\n");
            }
            catch (NotSupportedException)
            {
                Assert.Inconclusive("The temporary folder is not on NTFS.");
            }

            FileOriginService service = new(new Helper.FakeProcessRunner());

            FileOrigin? origin = await service.ReadAsync(file);
            Assert.AreEqual(3, origin?.ZoneId);
            Assert.AreEqual("https://h.example/a.zip", origin?.HostUrl);

            Assert.IsTrue(await service.RemoveAsync(file));
            Assert.IsNull(await service.ReadAsync(file));
            Assert.AreEqual("data", File.ReadAllText(file), "the file itself is untouched");
            Assert.IsTrue(await service.RemoveAsync(file), "removing a mark that is not there is not a failure");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public async Task Windows_AFileWithoutAStream_HasNoMark_AndAMissingFileNone()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string file = TempFile(out string folder);
        try
        {
            FileOriginService service = new(new Helper.FakeProcessRunner());
            Assert.IsNull(await service.ReadAsync(file));
            Assert.IsNull(await service.ReadAsync(Path.Combine(folder, "nothing.zip")));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public async Task Linux_ReadsTheXdgAttributes_ThroughGetfattr_WithDoubleDashBeforeThePath()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return;
        }

        string file = TempFile(out string folder);
        try
        {
            Helper.FakeProcessRunner runner = new();
            runner.Answer("getfattr --absolute-names --only-values -n user.xdg.origin.url", "https://o.example/a.zip\n");
            runner.Answer("getfattr --absolute-names --only-values -n user.xdg.referrer.url", "\"https://r.example/\"");
            FileOriginService service = new(runner);

            FileOrigin? origin = await service.ReadAsync(file);

            Assert.AreEqual("https://o.example/a.zip", origin?.OriginUrl);
            Assert.AreEqual("https://r.example/", origin?.ReferrerUrl);
            Assert.IsTrue(runner.Calls.All(c => c.Arguments.Contains("--")));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public async Task Linux_AMissingToolOrNoAttribute_MeansNoMark()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return;
        }

        string file = TempFile(out string folder);
        try
        {
            Helper.FakeProcessRunner runner = new();
            runner.Answer("getfattr", string.Empty, -1, "not found");
            FileOriginService service = new(runner);

            Assert.IsNull(await service.ReadAsync(file));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public async Task Linux_RemovesOnlyTheAttributesThatAreSet()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return;
        }

        string file = TempFile(out string folder);
        try
        {
            Helper.FakeProcessRunner runner = new();
            runner.Answer("getfattr --absolute-names --only-values -n user.xdg.origin.url", "https://o.example/");
            runner.Answer("getfattr --absolute-names --only-values -n user.xdg.referrer.url", string.Empty, 1, "No such attribute");
            runner.Answer("setfattr -x user.xdg.origin.url", string.Empty);
            FileOriginService service = new(runner);

            Assert.IsTrue(await service.RemoveAsync(file));
            Assert.AreEqual(1, runner.Calls.Count(c => c.FileName == "setfattr"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
