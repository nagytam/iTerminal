namespace iTerminal.Core.Services;

/// <summary>
/// The shell command lines the SSH drive sends. Every path is a single operand in POSIX single quotes, and the
/// operands follow <c>--</c> where the command takes it, so a name with a space, a <c>;</c>, a <c>$(...)</c> or a
/// leading dash reaches the command as one plain argument. Kept apart from the service so the text can be tested
/// without a server.
/// </summary>
internal static class SshCommands
{
    /// <summary>Quotes one path for the remote POSIX shell: <c>'</c> inside it becomes <c>'\''</c>.</summary>
    internal static string Quote(string path) => "'" + path.Replace("'", "'\\''") + "'";

    /// <summary><c>cp</c>; <paramref name="noClobber"/> adds <c>-n</c> so an existing target is kept.</summary>
    internal static string Copy(string source, string target, bool noClobber) =>
        $"cp -r{NoClobber(noClobber)} -- {Quote(source)} {Quote(target)}";

    /// <summary><c>mv</c>; <paramref name="noClobber"/> adds <c>-n</c> so an existing target is kept.</summary>
    internal static string Move(string source, string target, bool noClobber) =>
        $"mv{NoClobber(noClobber)} -- {Quote(source)} {Quote(target)}";

    internal static string RemoveFile(string path) => $"rm -- {Quote(path)}";

    internal static string RemoveTree(string path) => $"rm -R -- {Quote(path)}";

    /// <summary><c>rmdir</c>: removes an empty folder only, so a non-recursive delete never takes a folder's contents.</summary>
    internal static string RemoveEmptyFolder(string path) => $"rmdir -- {Quote(path)}";

    internal static string List(string path) => $"ls -la -- {Quote(path)}";

    internal static string TestFile(string path) => $"test -f {Quote(path)} && echo 1 || echo 0";

    internal static string TestFolder(string path) => $"test -d {Quote(path)} && echo 1 || echo 0";

    internal static string MakeFolder(string path) => $"mkdir -- {Quote(path)}";

    /// <summary>The working directory change that starts a user command. The command itself is the user's own shell text.</summary>
    internal static string ChangeDirectory(string path) => $"cd {Quote(path)};";

    private static string NoClobber(bool noClobber) => noClobber ? " -n" : string.Empty;
}
