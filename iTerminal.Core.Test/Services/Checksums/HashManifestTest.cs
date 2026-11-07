using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace iTerminal.Core.Test.Services.Checksums;

/// <summary>Creating a SHA256SUMS-style manifest for a tree and checking a folder against one.</summary>
[TestClass]
public class HashManifestTest
{
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private string _root = null!;

    [TestInitialize]
    public void CreateSandbox()
    {
        _root = Directory.CreateTempSubdirectory("iTerminal.Manifest.").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "sub", "deep"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "abc");
        File.WriteAllText(Path.Combine(_root, "sub", "b.txt"), "bravo");
        File.WriteAllText(Path.Combine(_root, "sub", "deep", "c.txt"), "charlie");
    }

    [TestCleanup]
    public void DeleteSandbox() => Directory.Delete(_root, recursive: true);

    private Url Folder => new(_root + Path.DirectorySeparatorChar);

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static IFileSystem NoFileSystem => Mock.Of<IFileSystem>();

    [Timeout(60_000)]
    [TestMethod]
    public async Task AWholeFolder_IsListedWithRelativePaths_AndHashedWithSha256ByDefault()
    {
        ManifestCreated created = await HashManifest.CreateAsync(Folder, [], NoFileSystem, ChecksumAlgorithm.Sha256);

        Assert.AreEqual("SHA256SUMS", created.FileName);
        Assert.AreEqual(3, created.Files);
        Assert.AreEqual(0, created.Failures.Count);
        Assert.AreEqual(
            $"{AbcSha256} *a.txt\n{Sha256("bravo")} *sub/b.txt\n{Sha256("charlie")} *sub/deep/c.txt\n",
            created.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MarkedRows_AreTheOnlyOnesCovered_AndAFolderStandsForItsTree()
    {
        Url[] roots = [new Url(Path.Combine(_root, "a.txt")), new Url(Path.Combine(_root, "sub") + Path.DirectorySeparatorChar)];

        ManifestCreated created = await HashManifest.CreateAsync(Folder, roots, NoFileSystem, ChecksumAlgorithm.Sha256);

        StringAssert.Contains(created.Text, " *a.txt\n");
        StringAssert.Contains(created.Text, " *sub/b.txt\n");
        StringAssert.Contains(created.Text, " *sub/deep/c.txt\n");

        ManifestCreated onlyOne = await HashManifest.CreateAsync(Folder, [roots[0]], NoFileSystem, ChecksumAlgorithm.Sha256);
        Assert.AreEqual($"{AbcSha256} *a.txt\n", onlyOne.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheAlgorithmIsChosen_AndNamesTheFile()
    {
        ManifestCreated sha512 = await HashManifest.CreateAsync(Folder, [new Url(Path.Combine(_root, "a.txt"))], NoFileSystem, ChecksumAlgorithm.Sha512);
        ManifestCreated md5 = await HashManifest.CreateAsync(Folder, [new Url(Path.Combine(_root, "a.txt"))], NoFileSystem, ChecksumAlgorithm.Md5);

        Assert.AreEqual("SHA512SUMS", sha512.FileName);
        Assert.AreEqual(128, sha512.Text.IndexOf(' '));
        Assert.AreEqual("MD5SUMS", md5.FileName);
        StringAssert.StartsWith(md5.Text, "900150983cd24fb0d6963f7d28e17f72 *a.txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnExistingManifestOfTheSameName_IsNotPartOfTheNewOne()
    {
        File.WriteAllText(Path.Combine(_root, "SHA256SUMS"), "whatever");

        ManifestCreated created = await HashManifest.CreateAsync(Folder, [], NoFileSystem, ChecksumAlgorithm.Sha256);

        Assert.AreEqual(3, created.Files);
        Assert.IsFalse(created.Text.Contains("SHA256SUMS"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Write_SavesTheManifest_AndAcceptsAnIdenticalOne_ButNeverReplacesAnotherThatSaysSomethingElse()
    {
        ManifestCreated created = await HashManifest.CreateAsync(Folder, [], NoFileSystem, ChecksumAlgorithm.Sha256);
        string target = Path.Combine(_root, "SHA256SUMS");

        Assert.AreEqual(ManifestWriteOutcome.Saved, HashManifest.Write(Folder, created).Outcome);
        Assert.AreEqual(created.Text, File.ReadAllText(target));
        Assert.AreEqual(ManifestWriteOutcome.AlreadySaysSo, HashManifest.Write(Folder, created).Outcome);

        // The publisher's file: a different hash for a.txt. It must survive.
        string theirs = $"{new string('0', 64)}  a.txt\n";
        File.WriteAllText(target, theirs);
        (ManifestWriteOutcome outcome, string message, _) = HashManifest.Write(Folder, created);

        Assert.AreEqual(ManifestWriteOutcome.SaysSomethingElse, outcome);
        StringAssert.Contains(message, "not overwritten");
        Assert.AreEqual(theirs, File.ReadAllText(target));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnExistingManifestWithMoreRowsOrFewer_SaysSomethingElse()
    {
        ManifestCreated created = await HashManifest.CreateAsync(Folder, [], NoFileSystem, ChecksumAlgorithm.Sha256);
        string target = Path.Combine(_root, "SHA256SUMS");
        File.WriteAllText(target, created.Text + $"{new string('1', 64)}  gone.txt\n");

        Assert.AreEqual(ManifestWriteOutcome.SaysSomethingElse, HashManifest.Write(Folder, created).Outcome);

        File.WriteAllText(target, $"{AbcSha256}  a.txt\n");
        Assert.AreEqual(ManifestWriteOutcome.SaysSomethingElse, HashManifest.Write(Folder, created).Outcome);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatCannotBeRead_IsAFailure_AndTheOthersAreStillHashed()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("An exclusive lock does not stop a read on Unix.");
        }

        // Held open without sharing: Windows refuses the read.
        string locked = Path.Combine(_root, "sub", "b.txt");
        await using FileStream hold = new(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ManifestCreated created = await HashManifest.CreateAsync(Folder, [], NoFileSystem, ChecksumAlgorithm.Sha256);

        Assert.AreEqual(1, created.Failures.Count);
        Assert.AreEqual("sub/b.txt", created.Failures[0].Path);
        Assert.AreEqual(2, created.Files);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACleanFolder_VerifiesEveryRowOk()
    {
        ManifestCreated created = await HashManifest.CreateAsync(Folder, [], NoFileSystem, ChecksumAlgorithm.Sha256);
        HashManifest.Write(Folder, created);

        ManifestVerified verified = await HashManifest.VerifyAsync(new Url(Path.Combine(_root, "SHA256SUMS")), Folder, NoFileSystem);

        Assert.IsTrue(verified.IsClean);
        Assert.AreEqual(3, verified.Count(ManifestStatus.Ok));
        Assert.AreEqual(3, verified.Items.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Verify_ReportsChangedMissingAndExtraRows()
    {
        ManifestCreated created = await HashManifest.CreateAsync(Folder, [], NoFileSystem, ChecksumAlgorithm.Sha256);
        HashManifest.Write(Folder, created);
        File.AppendAllText(Path.Combine(_root, "a.txt"), "!");
        File.Delete(Path.Combine(_root, "sub", "b.txt"));
        File.WriteAllText(Path.Combine(_root, "sub", "deep", "new.txt"), "n");

        List<ManifestItem> seen = [];
        ManifestVerified verified = await HashManifest.VerifyAsync(new Url(Path.Combine(_root, "SHA256SUMS")), Folder, NoFileSystem, onItem: seen.Add);

        Assert.IsFalse(verified.IsClean);
        Assert.AreEqual(ManifestStatus.Changed, verified.Items.Single(i => i.Path == "a.txt").Status);
        Assert.AreEqual(ManifestStatus.Missing, verified.Items.Single(i => i.Path == "sub/b.txt").Status);
        Assert.AreEqual(ManifestStatus.Ok, verified.Items.Single(i => i.Path == "sub/deep/c.txt").Status);
        Assert.AreEqual(ManifestStatus.Extra, verified.Items.Single(i => i.Path == "sub/deep/new.txt").Status);
        Assert.AreEqual(verified.Items.Count, seen.Count, "every row is reported as it is decided");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Verify_ReadsTheBsdStyle_AndEachRowsOwnAlgorithm()
    {
        string manifest = Path.Combine(_root, "BSD.txt");
        File.WriteAllText(manifest, $"SHA256 (a.txt) = {AbcSha256}\nMD5 (sub/b.txt) = {Convert.ToHexStringLower(MD5.HashData("bravo"u8))}\n# note\n");

        ManifestVerified verified = await HashManifest.VerifyAsync(new Url(manifest), Folder, NoFileSystem);

        Assert.AreEqual(2, verified.Count(ManifestStatus.Ok));
        Assert.AreEqual(1, verified.Count(ManifestStatus.Extra), "c.txt is extra");
        Assert.IsFalse(verified.Items.Any(i => i.Path == "BSD.txt"), "the manifest is not listed as extra");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APathThatLeavesTheFolder_IsNeverRead()
    {
        string outside = Path.Combine(Path.GetDirectoryName(_root)!, "outside-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outside, "abc");
        try
        {
            string manifest = Path.Combine(_root, "evil.sums");
            File.WriteAllText(manifest, $"{AbcSha256}  ../{Path.GetFileName(outside)}\n{AbcSha256}  /etc/passwd\n");

            ManifestVerified verified = await HashManifest.VerifyAsync(new Url(manifest), Folder, NoFileSystem);

            Assert.AreEqual(2, verified.Count(ManifestStatus.Unreadable));
            Assert.AreEqual(0, verified.Count(ManifestStatus.Ok));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatIsNoManifest_IsRefused()
    {
        string notes = Path.Combine(_root, "notes.txt");
        File.WriteAllText(notes, "nothing to see");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HashManifest.VerifyAsync(new Url(notes), Folder, NoFileSystem));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACancelledRun_Stops()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => HashManifest.CreateAsync(Folder, [], NoFileSystem, ChecksumAlgorithm.Sha256, null, cancellation.Token));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ProgressReachesOneHundredPercent_AndCountsEveryFile()
    {
        List<ManifestProgress> reports = [];
        await HashManifest.CreateAsync(Folder, [], NoFileSystem, ChecksumAlgorithm.Sha256, new Sync<ManifestProgress>(reports.Add));

        Assert.AreEqual(0, reports[0].Percent);
        Assert.AreEqual(100, reports[^1].Percent);
        Assert.AreEqual(3, reports[^1].FilesDone);
        Assert.AreEqual(3, reports[^1].FilesTotal);
        Assert.IsTrue(reports.Select(r => r.Percent).SequenceEqual(reports.Select(r => r.Percent).Order()), "never goes backwards");
    }

    [TestMethod]
    public void Percent_FallsBackToFilesWhenNoSizesAreKnown()
    {
        Assert.AreEqual(50, new ManifestProgress(1, 2, 0, 0).Percent);
        Assert.AreEqual(25, new ManifestProgress(1, 2, 25, 100).Percent);
        Assert.AreEqual(0, new ManifestProgress(0, 0, 0, 0).Percent);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnyDriveWorks_TheFilesAreStreamedThroughItsFileSystem()
    {
        Url folder = new("docker://web/etc/");
        Url file = new("docker://web/etc/app.conf");
        Url inner = new("docker://web/etc/d/");
        Url innerFile = new("docker://web/etc/d/x.conf");
        Dictionary<string, string> content = new() { [file.FullUrl] = "abc", [innerFile.FullUrl] = "bravo" };
        int reads = 0;
        Mock<IEntryListingFileSystem> drive = new();
        drive.Setup(d => d.ListAsync(It.IsAny<Url>(), It.IsAny<CancellationToken>())).ReturnsAsync((Url url, CancellationToken _) =>
            url.FullUrl == folder.FullUrl
                ? (IReadOnlyList<IEntry>)[new VirtualDriveEntry(file) { Size = 3 }, new VirtualDriveEntry(inner)]
                : [new VirtualDriveEntry(innerFile) { Size = 5 }]);
        drive.Setup(d => d.StreamToAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (Url url, Stream destination, IProgress<long>? _, CancellationToken _) =>
            {
                Interlocked.Increment(ref reads);
                await destination.WriteAsync(Encoding.UTF8.GetBytes(content[url.FullUrl]));
            });

        ManifestCreated created = await HashManifest.CreateAsync(folder, [], drive.Object, ChecksumAlgorithm.Sha256);

        Assert.AreEqual($"{AbcSha256} *app.conf\n{Sha256("bravo")} *d/x.conf\n", created.Text);
        Assert.AreEqual(2, reads, "one read per file");
        Assert.AreEqual(ManifestWriteOutcome.NotLocal, HashManifest.Write(folder, created).Outcome);
    }

    [TestMethod]
    public void ParseManifest_ReadsEveryFormat_AndNormalisesThePaths()
    {
        string text = $"﻿# a comment\r\n{AbcSha256} *./dist\\app.zip\r\n{new string('A', 32)}  plain.txt\r\nSHA256 (bsd file.txt) = {AbcSha256}\r\nnot a checksum line\r\n";

        IReadOnlyList<ChecksumService.ManifestEntry> entries = ChecksumService.ParseManifest(text);

        Assert.AreEqual(3, entries.Count);
        Assert.AreEqual("dist/app.zip", entries[0].Path);
        Assert.AreEqual(ChecksumAlgorithm.Md5, entries[1].Algorithm);
        Assert.AreEqual(new string('a', 32), entries[1].Hash);
        Assert.AreEqual("bsd file.txt", entries[2].Path);
    }

    [TestMethod]
    public void TheConventionalNames_AreRecognised()
    {
        Assert.IsTrue(HashManifest.IsManifestName("SHA256SUMS"));
        Assert.IsTrue(HashManifest.IsManifestName("sha512sums.txt"));
        Assert.IsFalse(HashManifest.IsManifestName("notes.txt"));
    }

    private sealed class Sync<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
