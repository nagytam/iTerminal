using System.Diagnostics;

namespace iTerminal.DevelopmentStudio.Services.Lsp;

/// <summary>A language server process the launcher started: its standard input and output carry the protocol.</summary>
public interface ILanguageServerProcess
{
    /// <summary>The server's standard input (what the client writes).</summary>
    Stream Input { get; }

    /// <summary>The server's standard output (what the client reads).</summary>
    Stream Output { get; }

    /// <summary>Kills the process and its children. Safe to call after the process has exited.</summary>
    void Kill();
}

/// <summary>Finds and starts a language server. The seam the end-to-end suite replaces, so a test never starts a real server.</summary>
public interface ILanguageServerLauncher
{
    /// <summary>The full path of the server program, or null when it is not installed.</summary>
    string? Locate();

    /// <summary>Starts the server in <paramref name="workingDirectory"/>, or returns null when it cannot be started.</summary>
    ILanguageServerProcess? Launch(string fileName, string workingDirectory);
}

/// <summary>
/// Starts a language server with <see cref="Process"/>, as <c>DebugSession</c> starts its adapter. The server is found on
/// the PATH only; its standard error is drained so a chatty server cannot block on a full pipe.
/// </summary>
public sealed class LanguageServerLauncher : ILanguageServerLauncher
{
    /// <summary>The program the C# language server is installed as (<c>dotnet tool install -g csharp-ls</c>).</summary>
    public const string CSharpLanguageServerProgram = "csharp-ls";

    public string? Locate()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        string[] names = OperatingSystem.IsWindows()
            ? [CSharpLanguageServerProgram + ".exe", CSharpLanguageServerProgram]
            : [CSharpLanguageServerProgram];
        foreach (string folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string name in names)
            {
                string candidate = Path.Combine(folder, name);
                try
                {
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                    // A PATH entry with characters the file system rejects is skipped.
                }
            }
        }

        return null;
    }

    public ILanguageServerProcess? Launch(string fileName, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        if (process is null) return null;
        _ = process.StandardError.ReadToEndAsync();
        return new Started(process);
    }

    private sealed class Started(Process process) : ILanguageServerProcess
    {
        public Stream Input { get; } = process.StandardInput.BaseStream;
        public Stream Output { get; } = process.StandardOutput.BaseStream;

        public void Kill()
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process has already gone.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The process has already gone.
            }
        }
    }
}
