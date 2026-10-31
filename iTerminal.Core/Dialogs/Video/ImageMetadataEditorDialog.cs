using iConsole.Localization;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;
using iTerminal.Core.Services.Images;

namespace iTerminal.Core.Dialogs.Video;

/// <summary>
/// Edits the text of a JPEG or a PNG: the description, artist, copyright and comment, prefilled with what the file has.
/// Enter saves; Esc closes without saving. A save runs in the background and the file is only replaced once the new text
/// is written. The other metadata (camera, exposure, GPS, picture) is kept as it is.
/// </summary>
public class ImageMetadataEditorDialog : Dialog
{
    private readonly string _path;
    private readonly TextBox _description;
    private readonly TextBox _artist;
    private readonly TextBox _copyright;
    private readonly TextBox _comment;
    private readonly Label _message;
    private readonly Action? _saved;
    private Task? _saving;
    private bool _saveSucceeded;
    private string? _failure;

    public ImageMetadataEditorDialog(
        string path,
        ImageMetadataEdit current,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        Action? saved = null)
        : base(iConsole.Localization.UiText.Get("Dialog.ImageText", "Image text"), themeService)
    {
        _path = path;
        _saved = saved;
        _description = Field("Description", current.Description, beepService, dateTimeProviderService, themeService);
        _artist = Field("Artist", current.Artist, beepService, dateTimeProviderService, themeService);
        _copyright = Field("Copyright", current.Copyright, beepService, dateTimeProviderService, themeService);
        _comment = Field("Comment", current.Comment, beepService, dateTimeProviderService, themeService);
        _message = new Label(ImageMetadataWriter.CanWrite(path) ? string.Empty : UiText.Get("Msg.TextOnlyJpegPng", "Text can only be written for JPEG and PNG files."), themeService);
        Title = string.Format(UiText.Get("Dialog.ImageTextTitle", "Image text — {0}"), Path.GetFileName(path));
        foreach (Control control in new Control[] { _description, _artist, _copyright, _comment, _message })
        {
            AddControl(control);
        }
    }

    /// <summary>The message under the fields: empty, why the file cannot be saved, or the save's error.</summary>
    public string Message => _message.Text;

    /// <summary>True while the text is being written.</summary>
    public bool IsSaving => _saving is not null && !_saving.IsCompleted;

    /// <summary>The save in progress, so a test can await it.</summary>
    public Task? SaveTask => _saving;

    private static TextBox Field(string label, string value, IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService) =>
        new(beepService, dateTimeProviderService, themeService) { Label = label, Text = value };

    /// <summary>The edit the fields hold, with the fields trimmed.</summary>
    public ImageMetadataEdit Edit() => new(
        _description.Text.Trim(),
        _artist.Text.Trim(),
        _copyright.Text.Trim(),
        _comment.Text.Trim());

    /// <summary>Saves the fields to the file in the background. Returns false, and says why, when the file cannot take them.</summary>
    public bool StartSave()
    {
        if (IsSaving)
        {
            return false;
        }

        if (!ImageMetadataWriter.CanWrite(_path))
        {
            _message.Text = UiText.Get("Msg.TextOnlyJpegPng", "Text can only be written for JPEG and PNG files.");
            StateHasChanged();
            return false;
        }

        ImageMetadataEdit edit = Edit();
        _message.Text = UiText.Get("Msg.Saving", "Saving…");
        _saving = Task.Run(() => ImageMetadataWriter.Write(_path, edit)).ContinueWith(task =>
        {
            // The result is shown on the idle pass, on the thread that owns the controls.
            _failure = task.Exception?.GetBaseException().Message;
            _saveSucceeded = task.IsCompletedSuccessfully;
        });
        StateHasChanged();
        return true;
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (_saving is { IsCompleted: true } && _message.Text == UiText.Get("Msg.Saving", "Saving…"))
        {
            if (_saveSucceeded)
            {
                // The viewer reloads the metadata here, on the thread that owns the controls, before the editor goes.
                _message.Text = string.Empty;
                _saved?.Invoke();
                Close();
            }
            else
            {
                _message.Text = _failure is null ? "Could not save the text." : $"Could not save the text: {_failure}";
            }

            StateHasChanged();
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Tab", UiText.Get("DialogHint.MoveBetweenFields", "Move between the fields"));
        yield return ("Enter", UiText.Get("DialogHint.SaveMetadata", "Save the metadata"));
        yield return ("Esc", UiText.Get("DialogHint.CloseWithoutSaving", "Close without saving"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Enter
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            StartSave();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(new Size(70, 9));
        }

        int width = Math.Max(0, (int)ViewSize.Width - 4);
        Control[] fields = [_description, _artist, _copyright, _comment];
        for (int i = 0; i < fields.Length; i++)
        {
            fields[i].Position = new Position(2, 1 + i);
            fields[i].ViewSize = new Size(width, 1);
            if (fields[i] is TextBox box)
            {
                box.ScrollableModel.ScrollableSize = new Size(box.MaxLength, 1);
                box.ScrollableModel.ViewSize = new Size(Math.Max(0, box.EditableWidth), 1);
            }
        }

        _message.Position = new Position(2, (int)ViewSize.Height - 3);
        _message.ViewSize = new Size(width, 1);
        base.OnInitializeLayout();
    }
}
