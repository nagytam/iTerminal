using iTerminal.Core.Services.Recycle;

namespace iTerminal.Core.Test.Helper;

/// <summary>
/// A Trash that moves what it is given into a folder of the test's own and records each call, so a
/// test can count how many separate trash moves a delete made without touching the real Trash.
/// </summary>
internal sealed class FakeTrashService(string trashFolder) : ITrashService
{
    public List<string> Trashed { get; } = [];

    public bool TryMoveToTrash(string path)
    {
        Trashed.Add(Path.TrimEndingDirectorySeparator(path));
        Directory.CreateDirectory(trashFolder);
        var target = Path.Combine(trashFolder, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path))
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target);
        }

        return true;
    }
}

/// <summary>A Trash that must never be asked to take anything: a test that reaches it has failed.</summary>
internal sealed class ForbiddenTrashService : ITrashService
{
    public bool TryMoveToTrash(string path) =>
        throw new InvalidOperationException($"The Trash must not be used here, but was asked to take {path}.");
}
