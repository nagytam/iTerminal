using iTerminal.Core.Services.AccessControl;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.AccessControl;

/// <summary>The POSIX side: getfacl output read back, and the exact setfacl / chown command lines written.</summary>
[TestClass]
public class UnixAccessControlServiceTest
{
    private const string Sample = """
        # file: srv/data
        # owner: alice
        # group: staff
        user::rwx
        user:bob:rw-             #effective:r--
        group::r-x
        group:ops:r-x
        mask::r--
        other::---
        default:user::rwx
        default:group::r-x
        default:other::---
        """;

    [Timeout(60_000)]
    [TestMethod]
    public void GetfaclOutputIsReadIntoEntriesWithOwnerAndGroup()
    {
        var info = UnixAccessControlService.Parse("/srv/data", true, Sample);

        Assert.AreEqual("alice", info.Owner);
        Assert.AreEqual("staff", info.Group);
        Assert.IsFalse(info.SupportsDeny);
        Assert.IsFalse(info.CanChangeInheritance);
        CollectionAssert.AreEqual(new[] { "owner", "user:bob", "group", "group:ops", "mask", "other" },
            info.Entries.Where(e => e.Scope is null).Select(e => e.Principal).ToArray());
        Assert.AreEqual("rw- (effective r--)", info.Entries.Single(e => e.Principal == "user:bob").Rights);
        Assert.AreEqual(3, info.Entries.Count(e => e.Scope == "default for new items"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PrincipalsBecomeSetfaclSpecs()
    {
        Assert.AreEqual("u::", UnixAccessControlService.Spec("owner"));
        Assert.AreEqual("g::", UnixAccessControlService.Spec("group"));
        Assert.AreEqual("o::", UnixAccessControlService.Spec("other"));
        Assert.AreEqual("m::", UnixAccessControlService.Spec("mask"));
        Assert.AreEqual("u:bob", UnixAccessControlService.Spec("bob"));
        Assert.AreEqual("u:bob", UnixAccessControlService.Spec("user:bob"));
        Assert.AreEqual("g:ops", UnixAccessControlService.Spec("group:ops"));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("bob,u:evil:rwx")]
    [DataRow("-R")]
    [DataRow("a b")]
    [DataRow("")]
    public void ANameThatCouldBeAnotherEntryOrAnOptionIsRefused(string name)
    {
        Assert.ThrowsExactly<ArgumentException>(() => UnixAccessControlService.Spec("user:" + name));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetWritesAnEntryAndAFolderAlsoGetsADefaultEntry()
    {
        var runner = new FakeProcessRunner().Answer("setfacl", string.Empty);
        var service = new UnixAccessControlService(runner);
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            service.Set(folder, "group:ops", AclKind.Allow, "r-x", inheritToChildren: true);

            CollectionAssert.AreEqual(
                new[] { $"setfacl -m g:ops:r-x -- {folder}", $"setfacl -d -m g:ops:r-x -- {folder}" },
                runner.Calls.Select(c => c.CommandLine).ToArray());
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFileGetsNoDefaultEntry()
    {
        var runner = new FakeProcessRunner().Answer("setfacl", string.Empty);
        var service = new UnixAccessControlService(runner);

        service.Set("/tmp/some-file", "bob", AclKind.Allow, "rw-", inheritToChildren: true);

        Assert.AreEqual(1, runner.Calls.Count);
        StringAssert.StartsWith(runner.Calls[0].CommandLine, "setfacl -m u:bob:rw- --");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DenyAndABadPermissionAreRefusedBeforeAnythingRuns()
    {
        var runner = new FakeProcessRunner();
        var service = new UnixAccessControlService(runner);

        Assert.ThrowsExactly<NotSupportedException>(() => service.Set("/tmp/x", "bob", AclKind.Deny, "r--", false));
        Assert.ThrowsExactly<ArgumentException>(() => service.Set("/tmp/x", "bob", AclKind.Allow, "rwxx", false));
        Assert.ThrowsExactly<NotSupportedException>(() => service.SetInheritance("/tmp/x", true));
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheOwnerEntryCannotBeRemoved()
    {
        var service = new UnixAccessControlService(new FakeProcessRunner());

        Assert.ThrowsExactly<ArgumentException>(() => service.Remove("/tmp/x", "owner", AclKind.Allow));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ChownIsGivenOwnerAndGroupTogetherAndOnlyWhatChanged()
    {
        var runner = new FakeProcessRunner().Answer("chown", string.Empty);
        var service = new UnixAccessControlService(runner);

        service.SetOwner("/tmp/x", "bob", "ops");
        service.SetOwner("/tmp/x", null, "ops");
        service.SetOwner("/tmp/x", "bob", null);
        service.SetOwner("/tmp/x", " ", " ");

        CollectionAssert.AreEqual(
            new[] { "chown bob:ops -- ", "chown :ops -- ", "chown bob -- " },
            runner.Calls.Select(c => c.CommandLine[..(c.CommandLine.IndexOf("-- ", StringComparison.Ordinal) + 3)]).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APermissionRefusalSaysWhoMayChangeIt()
    {
        var runner = new FakeProcessRunner().Answer("chown", string.Empty, exitCode: 1, stderr: "chown: changing ownership of 'x': Operation not permitted");
        var service = new UnixAccessControlService(runner);

        var exception = Assert.ThrowsExactly<UnauthorizedAccessException>(() => service.SetOwner("/tmp/x", "bob", null));

        StringAssert.Contains(exception.Message, "root");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutGetfaclTheModeBitsAreShownWithANote()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("File modes are read on Unix only.");
        }

        var runner = new FakeProcessRunner()
            .Answer("getfacl", string.Empty, exitCode: -1, stderr: "Cannot start getfacl: not found")
            .Answer("stat", "alice:staff\n");
        var file = Path.GetTempFileName();
        try
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            var info = new UnixAccessControlService(runner).Read(file);

            Assert.AreEqual("alice", info.Owner);
            CollectionAssert.AreEqual(new[] { "rw-", "r--", "---" }, info.Entries.Select(e => e.Rights).ToArray());
            StringAssert.Contains(info.Note, "getfacl is not installed");
        }
        finally
        {
            File.Delete(file);
        }
    }
}
