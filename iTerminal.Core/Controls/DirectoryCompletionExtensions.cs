using iConsole.Controls;
using iConsole.Extensions;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls;

public static class DirectoryCompletionExtensions
{
    /// <summary>
    /// Gives a path box live completion from the folders on disk (<see cref="DirectoryCompletionModel"/>). With no
    /// <paramref name="lister"/> the box is returned as it is: a dialog built without one (a test constructing it by hand)
    /// simply has no completion.
    /// </summary>
    public static TextBox WithDirectoryCompletion(
        this TextBox textBox,
        IDirectoryLister? lister,
        DirectoryCompletionMode mode = DirectoryCompletionMode.FoldersOnly,
        IEnumerable<string>? fileExtensions = null) =>
        lister is null ? textBox : textBox.WithAutoCompletion(new DirectoryCompletionModel(lister, mode, null, fileExtensions));
}
