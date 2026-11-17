using iConsole.Data;
using iConsole.Dialogs;

namespace iConsole.Viewers;

/// <summary>
/// A plug-in that takes files: either a viewer (F3) or an editor (F4). Handlers are offered the
/// file in <see cref="Priority"/> order, highest first; equal priorities keep the order they were
/// registered in. A plug-in registered in the container is offered a file before the built-in
/// viewers and editors, so it can take over a format the application already reads.
/// </summary>
public interface IFileHandler
{
    /// <summary>Higher is offered the file first. The built-in handlers use 0.</summary>
    int Priority { get; }

    /// <summary>
    /// Whether this handler takes the file, judged from its name or path alone. It runs on the key
    /// path (CanExecute and the menus), so it must not read the file.
    /// </summary>
    bool IsCandidate(Url url);
}

/// <summary>
/// A viewer plug-in. The command asks <see cref="IsCandidate"/>, then <see cref="CanOpenAsync"/>,
/// and the first handler that says yes shows the file.
/// </summary>
public interface IFileViewer : IFileHandler
{
    /// <summary>
    /// Confirms a candidate, typically by reading the file's header. The command awaits it on the
    /// key path, so a read must go to a worker thread. Answers yes by default.
    /// </summary>
    Task<bool> CanOpenAsync(Url url) => Task.FromResult(true);

    /// <summary>Shows the file. Dialogs go through <paramref name="host"/>, never <c>Control</c> directly.</summary>
    Task OpenAsync(Url url, IFileOpenHost host);
}

/// <summary>An editor plug-in (F4). The first editor whose <see cref="IFileHandler.IsCandidate"/> holds opens the file.</summary>
public interface IFileEditor : IFileHandler
{
    /// <summary>Opens the file for editing. Dialogs go through <paramref name="host"/>.</summary>
    Task OpenAsync(Url url, IFileOpenHost host);
}

/// <summary>What a viewer or editor needs from the command that opened it.</summary>
public interface IFileOpenHost
{
    /// <summary>Shows a dialog on the screen that asked for the file. Completes when the dialog closes.</summary>
    Task ShowDialogAsync(Dialog dialog);

    /// <summary>
    /// Shows the file in the application's own text viewer. A handler that looks at a file only after
    /// it has started (say, a certificate viewer that finds no certificate) falls back to this.
    /// </summary>
    Task ShowTextViewerAsync(Url? url);
}

/// <summary>An <see cref="IFileOpenHost"/> built from two delegates, for the commands that open files.</summary>
public sealed class FileOpenHost(Func<Dialog, Task> showDialog, Func<Url?, Task> showTextViewer) : IFileOpenHost
{
    public Task ShowDialogAsync(Dialog dialog) => showDialog(dialog);

    public Task ShowTextViewerAsync(Url? url) => showTextViewer(url);
}
