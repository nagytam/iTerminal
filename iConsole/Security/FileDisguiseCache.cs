using System.Collections.Concurrent;

namespace iConsole.Security;

/// <summary>
/// The kinds read from files' leading bytes so far, by full path. The listing only looks a path up here — it
/// never reads a file — and whoever read the bytes on a background task stores the answer. Small and
/// bounded: when it fills it is emptied, and the cursor rows are simply read again.
/// </summary>
public static class FileDisguiseCache
{
    internal const int Capacity = 1024;
    private static readonly ConcurrentDictionary<string, FileKind> Kinds = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string path, out FileKind kind) => Kinds.TryGetValue(path, out kind);

    public static void Set(string path, FileKind kind)
    {
        if (Kinds.Count >= Capacity)
        {
            Kinds.Clear();
        }

        Kinds[path] = kind;
    }

    public static void Remove(string path) => Kinds.TryRemove(path, out _);

    public static void Clear() => Kinds.Clear();

    /// <summary>The findings for a file name, plus the mismatch when its bytes have been read and contradict it.</summary>
    public static DisguiseFindings Findings(string path, string name)
    {
        DisguiseFindings findings = FileDisguise.AnalyzeName(name);
        if (TryGet(path, out FileKind kind) && FileDisguise.IsMismatch(name, kind))
        {
            findings |= DisguiseFindings.TypeMismatch;
        }

        return findings;
    }
}
