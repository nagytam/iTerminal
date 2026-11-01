using iConsole.Data;
using iTerminal.Core.Model;

namespace iTerminal.Core.Test.Model;

[TestClass]
public class TerminalCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesDefaultValues()
    {
        var command = new TerminalCommand();
        Assert.IsNotNull(command);
        // Default Url gets file:// scheme prefix
        Assert.IsTrue(((string)command.WorkingDirectoryUrl).StartsWith("file://") || (string)command.WorkingDirectoryUrl == string.Empty);
        Assert.AreEqual(string.Empty, command.Command);
        Assert.AreEqual(0, command.ExitCode);
        Assert.AreEqual(DateTime.MinValue, command.StartTime);
        Assert.AreEqual(DateTime.MinValue, command.ExitTime);
        Assert.IsNull(command.Output);
        Assert.IsNull(command.Error);
        Assert.IsNull(command.EnvironmentVariables);
        Assert.IsNull(command.CommandTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WorkingDirectoryUrl_SetAndGet()
    {
        var command = new TerminalCommand { WorkingDirectoryUrl = new Url("C:\\Users") };
        // Url may have file:// scheme prefix
        Assert.IsTrue(((string)command.WorkingDirectoryUrl).Contains("C:\\Users"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Command_SetAndGet()
    {
        var command = new TerminalCommand { Command = "dir" };
        Assert.AreEqual("dir", command.Command);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Command_CanBeEmpty()
    {
        var command = new TerminalCommand();
        Assert.AreEqual(string.Empty, command.Command);

        command.Command = "";
        Assert.AreEqual(string.Empty, command.Command);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ExitCode_SetAndGet()
    {
        var command = new TerminalCommand { ExitCode = 0 };
        Assert.AreEqual(0, command.ExitCode);

        command.ExitCode = 1;
        Assert.AreEqual(1, command.ExitCode);

        command.ExitCode = -1;
        Assert.AreEqual(-1, command.ExitCode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StartTime_SetAndGet()
    {
        var now = DateTime.Now;
        var command = new TerminalCommand { StartTime = now };
        Assert.AreEqual(now, command.StartTime);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ExitTime_SetAndGet()
    {
        var now = DateTime.Now;
        var command = new TerminalCommand { ExitTime = now };
        Assert.AreEqual(now, command.ExitTime);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Output_SetAndGet()
    {
        var output = "Command output text";
        var command = new TerminalCommand { Output = output };
        Assert.AreEqual(output, command.Output);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Error_SetAndGet()
    {
        var error = "Error message";
        var command = new TerminalCommand { Error = error };
        Assert.AreEqual(error, command.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EnvironmentVariables_SetAndGet()
    {
        var envVars = "PATH=C:\\;USER=Admin";
        var command = new TerminalCommand { EnvironmentVariables = envVars };
        Assert.AreEqual(envVars, command.EnvironmentVariables);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommandTask_SetAndGet()
    {
        var task = Task.CompletedTask;
        var command = new TerminalCommand { CommandTask = task };
        Assert.AreEqual(task, command.CommandTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Host_SetAndGet()
    {
        var command = new TerminalCommand { Host = "localhost" };
        Assert.AreEqual("localhost", command.Host);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Username_SetAndGet()
    {
        var command = new TerminalCommand { Username = "admin" };
        Assert.AreEqual("admin", command.Username);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AllProperties_CanBeSetAndRetrieved()
    {
        var startTime = DateTime.Now;
        var exitTime = DateTime.Now.AddSeconds(5);

        var command = new TerminalCommand
        {
            WorkingDirectoryUrl = new Url("C:\\"),
            Command = "cd temp && dir",
            ExitCode = 0,
            StartTime = startTime,
            ExitTime = exitTime,
            Output = "Output data",
            Error = null,
            EnvironmentVariables = "VAR1=value1",
            CommandTask = null,
            Host = "localhost",
            Username = "testuser"
        };

        Assert.IsTrue(((string)command.WorkingDirectoryUrl).Contains("C:\\"));
        Assert.AreEqual("cd temp && dir", command.Command);
        Assert.AreEqual(0, command.ExitCode);
        Assert.AreEqual(startTime, command.StartTime);
        Assert.AreEqual(exitTime, command.ExitTime);
        Assert.AreEqual("Output data", command.Output);
        Assert.IsNull(command.Error);
        Assert.AreEqual("VAR1=value1", command.EnvironmentVariables);
        Assert.IsNull(command.CommandTask);
        Assert.AreEqual("localhost", command.Host);
        Assert.AreEqual("testuser", command.Username);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MultipleCommands_AreIndependent()
    {
        var command1 = new TerminalCommand { Command = "dir" };
        var command2 = new TerminalCommand { Command = "ls" };

        command1.ExitCode = 0;
        command2.ExitCode = 1;

        Assert.AreEqual("dir", command1.Command);
        Assert.AreEqual("ls", command2.Command);
        Assert.AreEqual(0, command1.ExitCode);
        Assert.AreEqual(1, command2.ExitCode);
    }
}
