using iConsole.Data;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Docker;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;
using System.Formats.Tar;
using System.Text;

namespace iTerminal.Core.Test.Services.Docker;

/// <summary>
/// The docker:// drive is the docker CLI's output read back, so these tests feed it recorded output
/// — GNU and BusyBox <c>ls</c>, <c>docker ps</c>, tar streams — through a fake process runner. No
/// daemon is needed, and the command lines the drive sends are pinned as well.
/// </summary>
[TestClass]
public class DockerDriveServiceTest
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0);

    [TestMethod]
    public void ContainersAreListedRunningFirstWithTheirState()
    {
        var output =
            "db\texited\tExited (0) 2 days ago\tpostgres:16\t2026-10-01 09:15:00 +0200 CEST\n" +
            "web,web-alias\trunning\tUp 3 hours\tnginx:latest\t2026-10-04 08:00:00 +0200 CEST\n";

        var entries = DockerDriveService.ParseContainers(output);

        CollectionAssert.AreEqual(new[] { "web", "db" }, entries.Select(e => e.DisplayName).ToArray());
        Assert.AreEqual("docker://web/", entries[0].Url.FullUrl, "the first name addresses the container");
        Assert.AreEqual("Up", entries[0].FileSize);
        Assert.AreEqual("Exited", entries[1].FileSize);
        Assert.AreEqual("2026-10-01", entries[1].Date);
        StringAssert.Contains(((VirtualDriveEntry)entries[0]).Detail, "nginx:latest");
    }

    [TestMethod]
    public void GnuLsIsReadIntoFoldersThenFiles()
    {
        var output =
            "total 24\n" +
            "drwxr-xr-x 2 0 0 4096 Sep 30 10:11 conf.d\n" +
            "-rw-r--r-- 1 0 0 1007 Aug 14  2025 mime types\n" +
            "-rw-r--r-- 1 0 0  648 Oct  4 23:59 nginx.conf\n" +
            "crw-rw-rw- 1 0 0 1, 3 Oct  5 08:00 null\n";

        var entries = DockerDriveService.ParseListing(output, "web", "etc/nginx", Now);

        CollectionAssert.AreEqual(new[] { "conf.d", "mime types", "nginx.conf", "null" }, entries.Select(e => e.DisplayName).ToArray());
        Assert.AreEqual("docker://web/etc/nginx/conf.d/", entries[0].Url.FullUrl);
        Assert.AreEqual(PathType.Folder, entries[0].Url.PathType);
        Assert.AreEqual("docker://web/etc/nginx/mime types", entries[1].Url.FullUrl, "a space stays part of the name");
        Assert.AreEqual("2025-08-14", entries[1].Date, "a year means an old file");
        Assert.AreEqual(648, ((VirtualDriveEntry)entries[2]).Size);
        Assert.AreEqual("2026-10-04", entries[2].Date);
        Assert.AreEqual("23:59", entries[2].Time);
    }

    [TestMethod]
    public void BusyBoxLsIsReadToo()
    {
        // BusyBox pads differently and prints the link count right-aligned; the shape is the same.
        var output =
            "drwxr-xr-x    2 0        0             4096 Sep 26 18:30 bin\n" +
            "lrwxrwxrwx    1 0        0               12 Sep 26 18:30 dangling -> /nowhere\n" +
            "-rwxr-xr-x    1 0        0           824208 Sep 26 18:30 busybox\n";

        var entries = DockerDriveService.ParseListing(output, "alpine", string.Empty, Now);

        CollectionAssert.AreEqual(new[] { "bin", "busybox", "dangling" }, entries.Select(e => e.DisplayName).ToArray());
        Assert.AreEqual("docker://alpine/bin/", entries[0].Url.FullUrl);
    }

    [TestMethod]
    public void ATimeLaterThanNowBelongsToLastYear()
    {
        var entries = DockerDriveService.ParseListing("-rw-r--r-- 1 0 0 1 Dec 24 18:00 gift\n", "c", string.Empty, Now);

        Assert.AreEqual("2025-12-24", entries.Single().Date);
    }

    [TestMethod]
    public async Task AFolderIsListedWithLsInsideTheContainer()
    {
        var runner = new FakeProcessRunner().Answer("docker exec web ls", "-rw-r--r-- 1 0 0 5 Oct  5 08:00 a.txt\n");
        var service = new DockerDriveService(runner);

        var entries = await service.ListAsync(new Url("docker://web/etc/"), CancellationToken.None);

        Assert.AreEqual("a.txt", entries.Single().DisplayName);
        CollectionAssert.AreEqual(new[] { "exec", "web", "ls", "-lAnL", "--", "/etc" }, runner.Calls.Single().Arguments.ToArray());
    }

    [TestMethod]
    public async Task AStoppedDaemonIsSaidInWords()
    {
        var runner = new FakeProcessRunner().Answer("docker ps", string.Empty, exitCode: 1,
            stderr: "error during connect: Get \"http://%2F%2F.%2Fpipe%2FdockerDesktopLinuxEngine/v1.51/containers/json\": open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified.");
        var service = new DockerDriveService(runner);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ListAsync(new Url(Container.DOCKER), CancellationToken.None));

        StringAssert.StartsWith(exception.Message, "Docker is not running");
    }

    [TestMethod]
    public async Task AStoppedContainerIsSaidInWords()
    {
        var runner = new FakeProcessRunner().Answer("docker exec", string.Empty, exitCode: 1,
            stderr: "Error response from daemon: container 4f2a is not running");
        var service = new DockerDriveService(runner);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ListAsync(new Url("docker://db/"), CancellationToken.None));

        StringAssert.Contains(exception.Message, "stopped");
    }

    [TestMethod]
    public async Task AFileIsReadOutOfTheTarDockerCpWrites()
    {
        var runner = new FakeProcessRunner().AnswerBytes("docker cp web:/etc/hostname -", Tar("hostname", "web-1\n"));
        var service = new DockerDriveService(runner);

        var file = await service.ReadFileAsync(new Url("docker://web/etc/hostname"));

        Assert.AreEqual("web-1\n", Encoding.UTF8.GetString(file!.Content!));
    }

    [TestMethod]
    public async Task AReaderThatStopsEarlyStopsDockerCpWithItsOwnError()
    {
        // 4 MB of file behind the tar header; the destination takes 64 KB and refuses the rest,
        // the way the quick view does. The read must end with the destination's error, promptly,
        // rather than draining the whole file first.
        var runner = new FakeProcessRunner().AnswerBytes("docker cp web:/var/log/big.log -", Tar("big.log", new string('x', 4 * 1024 * 1024)));
        var service = new DockerDriveService(runner);
        var destination = new RefusingStream(64 * 1024);

        await Assert.ThrowsExactlyAsync<RefusingStream.FullException>(
            () => service.StreamToAsync(new Url("docker://web/var/log/big.log"), destination, null, CancellationToken.None));

        Assert.AreEqual(64 * 1024, destination.Length);
    }

    [TestMethod]
    public async Task AFailedDockerCpIsSaidInTheToolsWords()
    {
        var runner = new FakeProcessRunner().AnswerBytes("docker cp", [], exitCode: 1,
            stderr: "Error response from daemon: Could not find the file /etc/nope in container web");
        var service = new DockerDriveService(runner);

        var exception = await Assert.ThrowsExactlyAsync<IOException>(() => service.ReadFileAsync(new Url("docker://web/etc/nope")));

        StringAssert.Contains(exception.Message, "Could not find the file");
    }

    /// <summary>Takes up to a limit, then throws — the shape of the preview's capped buffer.</summary>
    private sealed class RefusingStream(int limit) : MemoryStream
    {
        public sealed class FullException : IOException
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var room = limit - (int)Length;
            base.Write(buffer, offset, Math.Min(room, count));
            if (count > room)
            {
                throw new FullException();
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.ToArray(), 0, buffer.Length);
            return ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    public async Task AFileIsWrittenAsATarOnDockerCpsStdin()
    {
        var runner = new FakeProcessRunner().Answer("docker cp - web:/tmp", string.Empty);
        var service = new DockerDriveService(runner);

        await service.StreamFromAsync(new Url("docker://web/tmp/notes.txt"), new MemoryStream(Encoding.UTF8.GetBytes("hello")), null, CancellationToken.None);

        var call = runner.Calls.Single();
        CollectionAssert.AreEqual(new[] { "cp", "-", "web:/tmp" }, call.Arguments.ToArray());
        using var reader = new TarReader(new MemoryStream(call.StandardInput));
        var entry = reader.GetNextEntry()!;
        Assert.AreEqual("notes.txt", entry.Name);
        Assert.AreEqual("hello", new StreamReader(entry.DataStream!).ReadToEnd());
    }

    [TestMethod]
    public async Task DeletingAndCreatingRunInsideTheContainer()
    {
        var runner = new FakeProcessRunner().Answer("docker exec", string.Empty);
        var service = new DockerDriveService(runner);

        service.DeleteFolder(new Url("docker://web/tmp/cache/"), recursive: true, moveToRecycleBin: false);
        await service.CreateFolderAsync(new Url("docker://web/tmp/new/"));

        CollectionAssert.AreEqual(new[] { "exec", "web", "rm", "-rf", "--", "/tmp/cache" }, runner.Calls[0].Arguments.ToArray());
        CollectionAssert.AreEqual(new[] { "exec", "web", "mkdir", "-p", "--", "/tmp/new" }, runner.Calls[1].Arguments.ToArray());
    }

    [TestMethod]
    public void TheDriveRootTakesNoFiles()
    {
        var service = new DockerDriveService(new FakeProcessRunner());

        Assert.ThrowsExactly<IOException>(() => service.DeleteFile(new Url("docker://"), false));
    }

    [TestMethod]
    [DataRow("docker://", "", "")]
    [DataRow("docker://web/", "web", "")]
    [DataRow("docker://web/etc/nginx/", "web", "etc/nginx")]
    [DataRow("docker://web/etc/hosts", "web", "etc/hosts")]
    public void SplitSeparatesTheContainerFromThePath(string url, string container, string path)
    {
        Assert.AreEqual((container, path), DockerDriveService.Split(new Url(url)));
    }

    [TestMethod]
    public void AnInstalledButSilentCliFallsBackToItsFirstLine()
    {
        var message = DockerDriveService.DescribeFailure(new ProcessOutput(1, string.Empty, "\nunknown flag: --nope\nSee 'docker --help'."));

        Assert.AreEqual("unknown flag: --nope", message);
    }

    [TestMethod]
    public void AContainerRowKnowsItsStateAndIsNotAFile()
    {
        var entries = DockerDriveService.ParseContainers(
            "web\trunning\tUp 3 hours\tnginx:latest\t2026-10-04 08:00:00 +0200 CEST\n" +
            "db\texited\tExited (0) 2 days ago\tpostgres:16\t2026-10-01 09:15:00 +0200 CEST\n");

        var web = (DockerContainerEntry)entries[0];
        var db = (DockerContainerEntry)entries[1];
        Assert.IsTrue(web.IsRunning);
        Assert.IsFalse(db.IsRunning);
        Assert.AreEqual("postgres:16", db.Image);
        Assert.AreEqual("Exited (0) 2 days ago", db.Status);
        Assert.IsInstanceOfType<IDriveObjectEntry>(web, "no file operation may take a container for a folder");
    }

    [TestMethod]
    [DataRow(DockerContainerAction.Start, "docker start web")]
    [DataRow(DockerContainerAction.Stop, "docker stop web")]
    [DataRow(DockerContainerAction.Restart, "docker restart web")]
    public async Task AContainerIsOperatedByName(DockerContainerAction action, string commandLine)
    {
        var runner = new FakeProcessRunner().Answer("docker", "web\n");
        var service = new DockerDriveService(runner);

        await service.ControlAsync("web", action, CancellationToken.None);

        Assert.AreEqual(commandLine, runner.Calls.Single().CommandLine);
    }

    [TestMethod]
    public async Task AFailedActionIsSaidInWords()
    {
        var runner = new FakeProcessRunner().Answer("docker start", string.Empty, exitCode: 1,
            stderr: "error during connect: open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified.");
        var service = new DockerDriveService(runner);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ControlAsync("web", DockerContainerAction.Start, CancellationToken.None));

        StringAssert.Contains(exception.Message, "Docker is not running");
    }

    [TestMethod]
    public async Task TheLogMergesStdoutAndStderrInTheOrderTheyWereWritten()
    {
        var runner = new FakeProcessRunner().Answer(
            "docker logs --tail 200 --timestamps web",
            "2026-10-05T08:00:00.000000001Z starting\n2026-10-05T08:00:02.000000000Z listening on :80\n",
            stderr: "2026-10-05T08:00:01.000000000Z \u001b[33mwarning:\u001b[0m no config\tusing defaults\n");
        var service = new DockerDriveService(runner);

        var log = await service.GetLogsAsync("web", 200, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "starting", "warning: no config    using defaults", "listening on :80" },
            log.ToArray(),
            "by time, with the stamps, colour codes and tabs taken out");
    }

    [TestMethod]
    public void OnlyTheLastLinesAreKept()
    {
        var output = string.Concat(Enumerable.Range(0, 10).Select(i => $"2026-10-05T08:00:0{i}.000000000Z line {i}\n"));

        var log = DockerDriveService.MergeLogs(output, string.Empty, 3);

        CollectionAssert.AreEqual(new[] { "line 7", "line 8", "line 9" }, log.ToArray());
    }

    [TestMethod]
    public async Task ACommandRunsInsideTheContainerInTheGivenFolderWithAClosedStdin()
    {
        var runner = new FakeProcessRunner().AnswerBytes("docker exec -w /etc web sh -c", Encoding.UTF8.GetBytes("hostname\nmotd\n"));
        var service = new DockerDriveService(runner);

        var result = await service.ExecAsync("web", "/etc", "ls | sort", CancellationToken.None);

        Assert.AreEqual("hostname\nmotd\n", result.StandardOutput);
        var call = runner.Calls.Single();
        CollectionAssert.AreEqual(new[] { "exec", "-w", "/etc", "web", "sh", "-c", "ls | sort" }, call.Arguments.ToArray(),
            "the command is one argument to sh, never split or quoted by hand");
        Assert.AreEqual(0, call.StandardInput.Length);
    }

    [TestMethod]
    public void AContainersRootIsNeverDeleted()
    {
        var runner = new FakeProcessRunner().Answer("docker", string.Empty);
        var service = new DockerDriveService(runner);

        Assert.ThrowsExactly<IOException>(() => service.DeleteFolder(new Url("docker://web/"), recursive: true, moveToRecycleBin: false));
        Assert.ThrowsExactly<IOException>(() => service.DeleteFile(new Url("docker://web/"), moveToRecycleBin: false));

        Assert.AreEqual(0, runner.Calls.Count, "rm -rf -- / is what it would have run inside the container");
    }

    [TestMethod]
    public void ACommandsOwnStderrIsNotADockerProblem()
    {
        Assert.IsNull(DockerDriveService.DescribeDockerProblem(new ProcessOutput(3, string.Empty, "nginx is not running")));
        Assert.IsNotNull(DockerDriveService.DescribeDockerProblem(
            new ProcessOutput(1, string.Empty, "Error response from daemon: container 4f2a is not running")));
    }

    private static byte[] Tar(string name, string content)
    {
        using var buffer = new MemoryStream();
        using (var writer = new TarWriter(buffer, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            });
        }

        return buffer.ToArray();
    }
}
