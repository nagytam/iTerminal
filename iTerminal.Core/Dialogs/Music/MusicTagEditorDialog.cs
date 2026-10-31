using iConsole.Localization;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Dialogs.Music;

/// <summary>
/// Edits the tags of an MP3 or a FLAC: title, artist, album, year, track, genre and comment, prefilled with what the file
/// has. Enter saves (the year is blank or four digits, the track a number, optionally "n/total"); Esc closes without saving.
/// A save runs in the background and the file is only replaced once the new tags are written.
/// </summary>
public class MusicTagEditorDialog : Dialog
{
    private readonly string _path;
    private readonly TextBox _title;
    private readonly TextBox _artist;
    private readonly TextBox _album;
    private readonly TextBox _year;
    private readonly TextBox _track;
    private readonly TextBox _genre;
    private readonly TextBox _comment;
    private readonly Label _message;
    private readonly Action? _saved;
    private Task? _saving;

    public MusicTagEditorDialog(
        string path,
        MusicTagSet current,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        Action? saved = null)
        : base("Tags", themeService)
    {
        _path = path;
        _saved = saved;
        _title = Field("Title", current.Title, beepService, dateTimeProviderService, themeService);
        _artist = Field("Artist", current.Artist, beepService, dateTimeProviderService, themeService);
        _album = Field("Album", current.Album, beepService, dateTimeProviderService, themeService);
        // The year is the file's Year, or its recording date when that is all it has.
        _year = Field("Year", Value(current, "Year", "Recorded"), beepService, dateTimeProviderService, themeService);
        _track = Field("Track", Value(current, "Track"), beepService, dateTimeProviderService, themeService);
        _genre = Field("Genre", Value(current, "Genre"), beepService, dateTimeProviderService, themeService);
        _comment = Field("Comment", Value(current, "Comment"), beepService, dateTimeProviderService, themeService);
        _message = new Label(string.Empty, themeService);
        Title = "Tags — " + Path.GetFileName(path);
        foreach (Control control in new Control[] { _title, _artist, _album, _year, _track, _genre, _comment, _message })
        {
            AddControl(control);
        }
    }

    /// <summary>The message under the fields: empty, or why the edit cannot be saved, or the save's error.</summary>
    public string Message => _message.Text;

    /// <summary>True while the tags are being written.</summary>
    public bool IsSaving => _saving is not null && !_saving.IsCompleted;

    /// <summary>The save in progress, so a test can await it.</summary>
    public Task? SaveTask => _saving;

    private static string Value(MusicTagSet tags, params string[] names) =>
        names.Select(name => tags.Tags.FirstOrDefault(t => t.Name == name).Value).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty;

    private static TextBox Field(string label, string value, IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService) =>
        new(beepService, dateTimeProviderService, themeService) { Label = label, Text = value };

    /// <summary>The edit the fields hold, with the fields trimmed.</summary>
    public AudioTagEdit Edit() => new(
        _title.Text.Trim(),
        _artist.Text.Trim(),
        _album.Text.Trim(),
        _year.Text.Trim(),
        _track.Text.Trim(),
        _genre.Text.Trim(),
        _comment.Text.Trim());

    /// <summary>What is wrong with the fields, or null when they can be saved.</summary>
    public string? Validate()
    {
        AudioTagEdit edit = Edit();
        if (edit.Year.Length > 0 && !(edit.Year.Length == 4 && edit.Year.All(char.IsAsciiDigit)))
        {
            return "The year is four digits, or blank.";
        }

        if (edit.Track.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(edit.Track, @"^\d+(/\d+)?$"))
        {
            return "The track is a number, or number/total, or blank.";
        }

        return null;
    }

    /// <summary>Saves the fields to the file in the background. Returns false, and says why, when the fields cannot be saved.</summary>
    public bool StartSave()
    {
        if (IsSaving)
        {
            return false;
        }

        if (Validate() is { } problem)
        {
            _message.Text = problem;
            StateHasChanged();
            return false;
        }

        AudioTagEdit edit = Edit();
        _message.Text = UiText.Get("Msg.Saving", "Saving…");
        _saving = Task.Run(() => AudioTagWriter.Write(_path, edit)).ContinueWith(task =>
        {
            // The result is shown on the idle pass, on the thread that owns the controls.
            Failure = task.Exception?.GetBaseException().Message;
            Saved = task.IsCompletedSuccessfully;
        });
        StateHasChanged();
        return true;
    }

    /// <summary>Set when the background save has finished: whether it wrote the file, and the error it hit.</summary>
    private bool Saved { get; set; }

    private string? Failure { get; set; }

    public override void OnIdle()
    {
        base.OnIdle();
        if (_saving is { IsCompleted: true } && _message.Text == UiText.Get("Msg.Saving", "Saving…"))
        {
            if (Saved)
            {
                // The viewer reloads the tags here, on the thread that owns the controls, before the editor goes.
                _saved?.Invoke();
                Close();
            }
            else
            {
                _message.Text = Failure is null ? "Could not save the tags." : $"Could not save the tags: {Failure}";
            }

            StateHasChanged();
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Tab", UiText.Get("DialogHint.MoveBetweenTags", "Move between the tags"));
        yield return ("Enter", UiText.Get("DialogHint.SaveTags", "Save the tags"));
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
            CenterInParent(new Size(70, 13));
        }

        int width = Math.Max(0, (int)ViewSize.Width - 4);
        Control[] fields = [_title, _artist, _album, _year, _track, _genre, _comment];
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
