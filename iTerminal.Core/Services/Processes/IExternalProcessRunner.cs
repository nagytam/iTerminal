namespace iTerminal.Core.Services.Processes;

/// <summary>What a finished external process left behind. A process that could not be started at
/// all — the executable is not installed — reports exit code -1 and the reason in
/// <see cref="StandardError"/>, so callers have one shape to handle rather than two.</summary>
public sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    /// <summary>The first non-empty line of stderr, or of stdout when stderr is empty — what a footer has room for.</summary>
    public string FirstErrorLine =>
        (StandardError + "\n" + StandardOutput)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? $"exit code {ExitCode}";
}

/// <summary>
/// Runs command-line tools (git, docker) the drives are built on. Arguments are passed through
/// <c>ProcessStartInfo.ArgumentList</c>, so nothing is ever quoted by hand. Injectable so the drive
/// services can be tested against recorded output instead of an installed tool.
/// </summary>
public interface IExternalProcessRunner
{
    /// <summary>Runs to completion and returns stdout and stderr as UTF-8 text.</summary>
    Task<ProcessOutput> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs to completion, copying stdout byte for byte into <paramref name="destination"/> — for
    /// file contents and tar streams, which are not text. <paramref name="writeStandardInput"/>, when
    /// given, is handed stdin and must write everything the tool should read; stdin is closed after.
    /// The returned <see cref="ProcessOutput.StandardOutput"/> is always empty.
    /// </summary>
    Task<ProcessOutput> RunToStreamAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Stream destination,
        string? workingDirectory = null,
        Func<Stream, Task>? writeStandardInput = null,
        CancellationToken cancellationToken = default);
}
