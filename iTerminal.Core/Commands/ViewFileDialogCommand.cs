using iConsole.Commands;
using iConsole.Data;
using iConsole.Csv;
using iConsole.Dialogs;
using iConsole.Json;
using iConsole.Xml;
using iConsole.Interfaces;
using iTerminal.Core.Audio;
using iTerminal.Core.Dialogs.Dotnet;
using iTerminal.Core.Dialogs.Music;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Dialogs.Web;
using iTerminal.Core.Video;

namespace iTerminal.Core.Commands;

public class ViewFileDialogCommand(
    FileViewerDialog fileViewerDialog,
    IContentModelService contentModelService,
    SolutionViewerDialog? solutionViewerDialog = null,
    MusicFileViewerDialog? musicFileViewerDialog = null,
    VideoFileViewerDialog? videoFileViewerDialog = null,
    ImageFileViewerDialog? imageFileViewerDialog = null,
    XmlViewerDialog? xmlViewerDialog = null,
    JsonViewerDialog? jsonViewerDialog = null,
    CsvViewerDialog? csvViewerDialog = null,
    WebBrowserDialog? webBrowserDialog = null) : Command
{
    private readonly WebBrowserDialog? _webBrowserDialog = webBrowserDialog;
    private readonly VideoFileViewerDialog? _videoFileViewerDialog = videoFileViewerDialog;
    private readonly ImageFileViewerDialog? _imageFileViewerDialog = imageFileViewerDialog;
    private readonly FileViewerDialog _fileViewerDialog = fileViewerDialog;
    private readonly IContentModelService _contentModelService = contentModelService;
    private readonly SolutionViewerDialog? _solutionViewerDialog = solutionViewerDialog;
    private readonly MusicFileViewerDialog? _musicFileViewerDialog = musicFileViewerDialog;
    private readonly XmlViewerDialog? _xmlViewerDialog = xmlViewerDialog;
    private readonly JsonViewerDialog? _jsonViewerDialog = jsonViewerDialog;
    private readonly CsvViewerDialog? _csvViewerDialog = csvViewerDialog;

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
                    return IsVideoFile(url) || IsImageFile(url) || IsMusicFile(url) || IsSupportedFileType(url.Extension);
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

    /// <summary>Video and pictures are decoded by ffmpeg from a path, so like audio they need a real local file.</summary>
    private bool IsVideoFile(Url url) =>
        _videoFileViewerDialog is not null
        && VideoFileTypes.IsVideoFile(url.Path)
        && File.Exists(url.Path);

    private bool IsImageFile(Url url) =>
        _imageFileViewerDialog is not null
        && ImageFileTypes.IsImageFile(url.Path)
        && File.Exists(url.Path);

    private static bool IsHtmlFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".html" or ".htm" or ".xhtml";

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

        // An XML document opens in the XML viewer, from any drive: it is read through the file system like text.
        if (_xmlViewerDialog is not null
            && focusedFileUrl is not null
            && XmlFileTypes.IsXmlFile(focusedFileUrl.Path))
        {
            _xmlViewerDialog.Url = focusedFileUrl;
            await (Control?.ShowDialogAsync(_xmlViewerDialog) ?? Task.CompletedTask);
            return;
        }

        // An HTML page (.html, .htm, .xhtml) is meant to be browsed, not read as markup: it opens in the
        // Web Browser. The browser loads it from disk, so it needs a real local file.
        if (_webBrowserDialog is not null
            && focusedFileUrl is not null
            && IsHtmlFile(focusedFileUrl.Path)
            && File.Exists(focusedFileUrl.Path))
        {
            _webBrowserDialog.StartAddress = iTerminal.Core.Model.Web.WebAddress.FileUrl(focusedFileUrl.Path);
            await (Control?.ShowDialogAsync(_webBrowserDialog) ?? Task.CompletedTask);
            return;
        }

        // A CSV or TSV file opens in the grid viewer, from any drive: it is read through the file system like text.
        if (_csvViewerDialog is not null
            && focusedFileUrl is not null
            && CsvFileTypes.IsCsvFile(focusedFileUrl.Path))
        {
            _csvViewerDialog.Url = focusedFileUrl;
            await (Control?.ShowDialogAsync(_csvViewerDialog) ?? Task.CompletedTask);
            return;
        }

        // A JSON document opens in the JSON viewer, from any drive, like an XML one.
        if (_jsonViewerDialog is not null
            && focusedFileUrl is not null
            && JsonFileTypes.IsJsonFile(focusedFileUrl.Path))
        {
            _jsonViewerDialog.Url = focusedFileUrl;
            await (Control?.ShowDialogAsync(_jsonViewerDialog) ?? Task.CompletedTask);
            return;
        }

        // Ahead of the music viewer: .mp4 and .webm are in the audio list too, and a file that has a
        // picture is meant to be watched.
        if (_videoFileViewerDialog is not null
            && focusedFileUrl is not null
            && IsVideoFile(focusedFileUrl))
        {
            _videoFileViewerDialog.FilePath = focusedFileUrl.Path;
            await (Control?.ShowDialogAsync(_videoFileViewerDialog) ?? Task.CompletedTask);
            return;
        }

        if (_imageFileViewerDialog is not null
            && focusedFileUrl is not null
            && IsImageFile(focusedFileUrl))
        {
            _imageFileViewerDialog.FilePath = focusedFileUrl.Path;
            await (Control?.ShowDialogAsync(_imageFileViewerDialog) ?? Task.CompletedTask);
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
