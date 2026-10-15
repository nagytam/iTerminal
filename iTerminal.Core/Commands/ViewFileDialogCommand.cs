using iConsole.Commands;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iTerminal.Core.Audio;
using iTerminal.Core.Dialogs.Dotnet;
using iTerminal.Core.Dialogs.Music;

namespace iTerminal.Core.Commands;

public class ViewFileDialogCommand(
    FileViewerDialog fileViewerDialog,
    IContentModelService contentModelService,
    SolutionViewerDialog? solutionViewerDialog = null,
    MusicFileViewerDialog? musicFileViewerDialog = null) : Command
{
    private readonly FileViewerDialog _fileViewerDialog = fileViewerDialog;
    private readonly IContentModelService _contentModelService = contentModelService;
    private readonly SolutionViewerDialog? _solutionViewerDialog = solutionViewerDialog;
    private readonly MusicFileViewerDialog? _musicFileViewerDialog = musicFileViewerDialog;

    public override string Title => "View";
    public override string Description => "Open the selected file in the viewer.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F3, false, false, false);

    public override bool CanExecute()
    {
        if (!string.IsNullOrEmpty(_contentModelService.FocusedFileUrl?.Path))
        {
            try
            {
                var url = _contentModelService.FocusedFileUrl;
                var model = _contentModelService.GetFocusedModel();
                if (url is not null && model is not null && model.ExistsFile(url))
                {
                    return IsMusicFile(url) || IsSupportedFileType(url.Extension);
                }
            }
            catch
            {
            }
        }
        return false;
    }

    private static bool IsSupportedFileType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            "mp3" or "wav" or "ogg" or "flac" or
            "mp4" or "avi" or "mkv" or "mov" or
            "zip" or "tar" or "gz" or "7z" or "rar" or
            "bin" => false,
            _ => true
        };

    /// <summary>
    /// Audio files open in the music viewer, but only when they resolve to a real local file: the
    /// player decodes from a path, so a track on an SSH or SMB drive has nothing for it to open —
    /// the same restriction the solution viewer has.
    /// </summary>
    private bool IsMusicFile(Url url) =>
        _musicFileViewerDialog is not null
        && AudioDecoderFactory.IsAudioFile(url.Path)
        && File.Exists(url.Path);

    private static bool IsSolutionFile(string extension) =>
        extension.ToLowerInvariant() is "slnx" or "sln";

    public override async Task ExecuteAsync(object? args)
    {
        if (_contentModelService is null)
        {
            return;
        }

        var focusedFileUrl = _contentModelService.FocusedFileUrl;

        // Solution files open in the solution viewer (project list + dotnet build/test/run),
        // but only when they resolve to a real local file — dotnet can't act on a remote path.
        if (_solutionViewerDialog is not null
            && focusedFileUrl is not null
            && IsSolutionFile(focusedFileUrl.Extension)
            && File.Exists(focusedFileUrl.Path))
        {
            _solutionViewerDialog.SolutionPath = focusedFileUrl.Path;
            await (Control?.ShowDialogAsync(_solutionViewerDialog) ?? Task.CompletedTask);
            return;
        }

        if (_musicFileViewerDialog is not null
            && focusedFileUrl is not null
            && IsMusicFile(focusedFileUrl))
        {
            _musicFileViewerDialog.FilePath = focusedFileUrl.Path;
            await (Control?.ShowDialogAsync(_musicFileViewerDialog) ?? Task.CompletedTask);
            return;
        }

        _fileViewerDialog.Url = focusedFileUrl;
        await (Control?.ShowDialogAsync(_fileViewerDialog) ?? Task.CompletedTask);
    }
}
