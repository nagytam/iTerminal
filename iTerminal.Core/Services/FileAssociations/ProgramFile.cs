namespace iTerminal.Core.Services.FileAssociations;

/// <summary>
/// What Enter on a file with no program chosen needs: whether the file is a program that runs in the
/// command log, and how its name must be typed there so that the shell finds it.
/// </summary>
public static class ProgramFile
{
    private static readonly string[] WindowsProgramExtensions = [".exe", ".com", ".bat", ".cmd"];

    /// <summary>
    /// Whether <paramref name="path"/> is a program: a Windows program extension, or on Unix any execute bit.
    /// A script without its execute bit is not one, so Enter gives it to the system's default program instead.
    /// </summary>
    public static bool IsRunnable(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                return WindowsProgramExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
            }

            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & anyExecute) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The text that runs the program <paramref name="fileName"/> from the folder the command log starts in.
    /// Unix shells do not search the current folder, so the name gets <c>./</c>, and single quotes keep
    /// spaces and symbols literal. Windows takes the bare name, quoted only when it has a space.
    /// </summary>
    public static string CommandFor(string fileName)
    {
        if (OperatingSystem.IsWindows())
        {
            return fileName.Contains(' ') ? $"\"{fileName}\"" : fileName;
        }

        return "./'" + fileName.Replace("'", "'\\''") + "'";
    }
}
