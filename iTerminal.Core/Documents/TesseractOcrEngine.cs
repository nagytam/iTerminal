using System.Diagnostics;
using System.Text;

namespace iTerminal.Core.Documents;

/// <summary>
/// OCR through the Tesseract command-line tool, when it happens to be installed.
///
/// <para>Shelling out is the same bargain the audio decoders strike with ffmpeg, and it is the
/// right one for the same reasons: a managed OCR engine would be a large native dependency shipped
/// to every user for a feature most of them will never point at a scanned page, whereas anyone who
/// does want image search almost certainly has — or is happy to install — Tesseract. The rest of
/// the document index does not depend on it: an image with no OCR still contributes its file name
/// and its embedded metadata, which is what <see cref="Extractors.ImageTextExtractor"/> reads
/// regardless.</para>
///
/// <para>Availability is probed lazily and remembered, including the failure. The probe is a process
/// start, and an indexing pass over a folder of ten thousand photographs would otherwise pay for one
/// per file just to be told again that the tool is not there.</para>
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    /// <summary>
    /// How long one image is given. OCR on a large scan is genuinely slow, but a run that has not
    /// finished in this long has met something pathological, and the scan behind it has thousands
    /// more files to get through.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>The executable to run. Overridable so a test can point at a stub.</summary>
    public string ExecutableName { get; init; } = "tesseract";

    private readonly Lock _probeLock = new();
    private bool _probed;
    private bool _available;
    private string _displayName = "Tesseract (not installed)";

    public string DisplayName
    {
        get
        {
            EnsureProbed();
            return _displayName;
        }
    }

    public bool IsAvailable
    {
        get
        {
            EnsureProbed();
            return _available;
        }
    }

    private void EnsureProbed()
    {
        lock (_probeLock)
        {
            if (_probed)
            {
                return;
            }

            _probed = true;

            try
            {
                (int exitCode, string output) = Run(["--version"], TimeSpan.FromSeconds(10), CancellationToken.None);
                if (exitCode != 0)
                {
                    return;
                }

                _available = true;

                // "tesseract 5.3.4" on the first line; anything else and the bare name will do.
                string firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                _displayName = firstLine.Trim().Length > 0 ? firstLine.Trim() : "Tesseract";
            }
            catch
            {
                // Not installed, not on PATH, or blocked. All of them mean the same thing here.
            }
        }
    }

    public string Recognize(string imagePath, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return string.Empty;
        }

        try
        {
            // "stdout" as the output base is Tesseract's own way of saying "do not write a file";
            // anything else would litter the user's documents folder with .txt siblings.
            (int exitCode, string output) = Run([imagePath, "stdout"], Timeout, cancellationToken);
            return exitCode == 0 ? output : string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return string.Empty;
        }
    }

    private (int ExitCode, string Output) Run(string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = ExecutableName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };

        StringBuilder output = new();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };

        // Tesseract writes its version banner and its progress to stderr. The banner is what the
        // probe reads, so stderr has to be drained either way — an undrained pipe is how a process
        // that produces a lot of it deadlocks instead of finishing.
        StringBuilder error = new();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                error.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone, or not ours to kill.
            }

            return (-1, string.Empty);
        }

        cancellationToken.ThrowIfCancellationRequested();

        string text = output.ToString();
        return (process.ExitCode, text.Trim().Length > 0 ? text : error.ToString());
    }
}
