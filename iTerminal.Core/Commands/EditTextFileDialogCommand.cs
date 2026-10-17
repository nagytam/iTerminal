using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Core.Commands;

/// <summary>
/// F4: the selected file in the editor — the one the user chose for files like it ("Edit with…",
/// Ctrl+F4) when there is one, which can then be any kind of file, and the built-in text editor
/// otherwise. The choice is made here rather than by a second command ahead of this one, so the
/// panel, the hot key bar's button and the File menu's entry cannot disagree about what F4 does.
/// </summary>
public class EditTextFileDialogCommand(
    TextFileEditorDialog textFileEditorDialog,
    IContentModelService contentModelService,
    FileAssociationRunner? fileAssociations = null)
    : ShowDialogCommand<TextFileEditorDialog>(textFileEditorDialog)
{
    public override string Title => "Edit";
    public override string Description => "Open the selected text file in the editor.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F4, false, false, false);

    public override bool CanExecute()
    {
        var model = contentModelService.GetFocusedModel();
        var url = model?.GetFocusedEntry()?.Url;
        if (fileAssociations?.DefaultFor(url, FileAssociationKind.Edit) is not null)
        {
            return true;
        }

        if (url is not null && model is not null && model.ExistsFile(url))
        {
            var extension = url.Extension;
            if (IsSupportedFileType(extension))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The text formats F4 opens. Anything else is left to F3. The list used to stop at a handful of
    /// .NET and web formats, so F4 on a README.md, a .ini, a script or a .sql file did nothing at all
    /// but beep.
    /// </summary>
    internal static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "",
        // Documents and data
        "txt", "md", "markdown", "rst", "log", "csv", "tsv", "json", "jsonc", "xml", "yml", "yaml", "toml", "ini", "cfg", "conf", "config", "env",
        // .NET and build
        "cs", "csx", "csproj", "fs", "fsproj", "vb", "vbproj", "props", "targets", "sln", "slnx", "xaml", "razor", "cshtml", "resx",
        // Web
        "htm", "html", "css", "scss", "js", "mjs", "ts", "tsx", "jsx", "svg",
        // Scripts and other languages
        "sh", "bash", "zsh", "ps1", "psm1", "psd1", "cmd", "bat", "py", "rb", "go", "rs", "java", "kt", "c", "h", "cpp", "hpp", "sql", "lua",
        // Repository files
        "dockerignore", "editorconfig", "gitignore", "gitattributes", "gitmodules",
    };

    private static bool IsSupportedFileType(string extension) =>
        SupportedExtensions.Contains(extension.TrimStart('.'));

    public override async Task ExecuteAsync(object? args)
    {
        var model = contentModelService?.GetFocusedModel();
        var url = model?.GetFocusedEntry()?.Url;
        if (fileAssociations?.DefaultFor(url, FileAssociationKind.Edit) is { } association
            && FileAssociationRunner.LocalFilePath(url) is { } path)
        {
            _ = fileAssociations.Start(association, path);
            return;
        }

        await base.ExecuteAsync(args);

        // The editor may have changed the file's size and date; the panel only notices on a re-read.
        try
        {
            model?.Refresh(forceRefresh: true);
        }
        catch
        {
            // A panel that cannot be re-read must not turn a finished edit into a failure.
        }
    }

    protected override void OnBeforeShow(TextFileEditorDialog dialog)
    {
        dialog.Url = contentModelService.FocusedFileUrl;
    }
}
