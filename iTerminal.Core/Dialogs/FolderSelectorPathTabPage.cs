using iConsole.Localization;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Extensions;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model;
using iTerminal.Core.Model.Validations;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The page that reaches every drive kind the app has. The other pages each answer one question —
/// which drive, which favourite, which folder I was in before — and between them they still leave
/// whole schemes unreachable: an archive, an SMB share, a phone, the music drives. Rather than grow
/// a page per scheme, this one takes the URL itself, hands it to <see cref="Url"/> and lets the
/// content-model factory decide what it is. A scheme added to the app is reachable here the day it
/// is added, without touching this file.
///
/// It is the last tab rather than the first because typing a full URL is the fallback, not the
/// common case; the hint line under the box is what makes the schemes discoverable at all.
/// </summary>
public class FolderSelectorPathTabPage : FolderSelectorTabPage
{
    public TextBox TextBoxPath { get; private set; }
    private ErrorMessage _errorMessage { get; set; }
    private Label _schemesLabel { get; set; }
    private Button _goButton { get; set; }
    private Breadcrumb _crumbs { get; set; }

    /// <summary>Where each segment of the typed path ends: clicking crumb <c>i</c> cuts the text back to <c>_crumbPrefixes[i]</c>.</summary>
    private List<string> _crumbPrefixes = [];

    /// <summary>What has been typed, untrimmed. <see cref="GoToPathCommand"/> trims it.</summary>
    public string Path => TextBoxPath.Text;

    public FolderUrlValidationModel PathValidator { get; private set; }

    private ICommand _goToPathCommand { get; set; }
    protected override ICommand? SelectCommand => _goToPathCommand;
    protected override string SelectDescription => iConsole.Localization.UiText.Get("FolderSelector.Select.Path", "Go to the typed path or URL.");

    public FolderSelectorPathTabPage(
        IContentModelService contentModelService,
        FolderHistoryService folderHistoryService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        ISettingsService settingsService,
        IThemeService themeService)
        : base("6-Path", ConsoleKey.D6, themeService)
    {
        var completionModel = new FolderPathCompletionModel(settingsService, folderHistoryService);
        TextBoxPath = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Field.PathOrUrl", "Path or URL: "),
            MaxLength = 1024,
            Placeholder = UiText.Get("Placeholder.PathExample", UiText.Get("Placeholder.PathExample", "e.g. /home/me, C:\\Users, ssh://host/path")),
        }.WithAutoCompletion(completionModel);
        PathValidator = new FolderUrlValidationModel(TextBoxPath.TextModel, dateTimeProviderService);
        TextBoxPath.ValidationModel = PathValidator;
        _errorMessage = new ErrorMessage(PathValidator, themeService);

        _schemesLabel = new Label(GetSchemesHint(), themeService);

        _goToPathCommand = new GoToPathCommand(contentModelService, PathValidator);
        _goButton = new Button(iConsole.Localization.UiText.Get("Button.Go", "Go"), _goToPathCommand, themeService)
        {
            HotKeyChar = 'G'
        };

        // The typed path, one clickable crumb per folder: a click cuts the box back to that folder, which is
        // how one goes up a few levels without holding Backspace.
        _crumbs = new Breadcrumb(themeService);
        _crumbs.SegmentActivated += OnCrumbActivated;
        TextBoxPath.TextModel.PropertyChanged += (_, _) => UpdateCrumbs();

        AddControl(TextBoxPath);
        AddControl(_crumbs);
        AddControl(_errorMessage);
        AddControl(_schemesLabel);
        AddControl(_goButton);
    }

    /// <summary>
    /// The folders of a typed path or URL as (name, text up to and including it) pairs: a Windows path is its
    /// drive, then each folder; <c>/home/me</c> is <c>/</c>, <c>home</c>, <c>me</c>; <c>ssh://host/a/b</c> starts at
    /// <c>ssh://host</c>. Nothing is resolved or checked — it is only where the separators are.
    /// </summary>
    public static List<(string Name, string Prefix)> SplitPath(string text)
    {
        List<(string, string)> parts = [];
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return parts;
        }

        var start = 0;
        var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0)
        {
            var hostEnd = trimmed.IndexOf('/', schemeEnd + 3);
            var end = hostEnd < 0 ? trimmed.Length : hostEnd;
            parts.Add((trimmed[..end], trimmed[..end]));
            start = end;
        }
        else if (trimmed[0] is '/' or '\\')
        {
            parts.Add((trimmed[..1], trimmed[..1]));
            start = 1;
        }

        while (start < trimmed.Length)
        {
            if (trimmed[start] is '/' or '\\')
            {
                start++;
                continue;
            }

            var next = trimmed.IndexOfAny(['/', '\\'], start);
            var end = next < 0 ? trimmed.Length : next;
            var prefix = trimmed[..end];
            // A drive letter keeps its separator, so its crumb cuts back to the drive root and not to a relative "C:".
            if (parts.Count == 0 && end - start == 2 && trimmed[start + 1] == ':' && next >= 0)
            {
                prefix = trimmed[..(next + 1)];
            }

            parts.Add((trimmed[start..end], prefix));
            start = end;
        }

        return parts;
    }

    private void UpdateCrumbs()
    {
        var parts = SplitPath(TextBoxPath.Text ?? string.Empty);
        _crumbPrefixes = [.. parts.Select(part => part.Prefix)];
        _crumbs.Segments = [.. parts.Select(part => part.Name)];
    }

    private void OnCrumbActivated(object? sender, int index)
    {
        if (index >= 0 && index < _crumbPrefixes.Count)
        {
            TextBoxPath.Text = _crumbPrefixes[index];
            TextBoxPath.ScrollableModel.CursorPosition.X = TextBoxPath.Text.Length;
        }
    }

    /// <summary>
    /// Records a path that was actually navigated to, so it completes next time. Called by
    /// <see cref="GoToPathCommand"/> after the navigation, not while typing — a half-typed URL is
    /// not a place anyone wants offered back.
    /// </summary>
    public void RememberPath(string path)
    {
        TextBoxPath.AutoCompletion?.CompletionModel.InsertCompletion(path);
    }

    /// <summary>
    /// One line naming every scheme <see cref="FolderUrlValidationModel"/> accepts. Generated from
    /// that list rather than written out, so the hint cannot drift from what is actually allowed.
    /// </summary>
    public static string GetSchemesHint() =>
        "Tab completes. Schemes: " + string.Join(" ", FolderUrlValidationModel.SupportedSchemes)
        + " — or a plain local path.";

    public override void OnInitializeLayout()
    {
        var innerWidth = Math.Max(0, ViewSize.Width - 2);

        // One row high, because that is exactly what a TextBox draws. The height is not cosmetic: the
        // completion list is a Popup placed *below its owner*, so a box claiming the height of the
        // whole page put its suggestions at the bottom of the screen, a dozen rows from the text they
        // belonged to. The tall box (and the error message six rows down) is a leftover from when the
        // list was drawn inside the box's own rectangle rather than floating over the page.
        TextBoxPath.Position = new Position(1, 0);
        TextBoxPath.ViewSize = new Size(innerWidth, 1);
        TextBoxPath.ScrollableModel.ScrollableSize = new Size(TextBoxPath.MaxLength, 1);
        TextBoxPath.ScrollableModel.ViewSize = new Size(Math.Max(0, TextBoxPath.EditableWidth), 1);

        _crumbs.Position = new Position(1, 2);
        _crumbs.ViewSize = new Size(innerWidth, 1);

        _errorMessage.Position = new Position(1, 1);
        _errorMessage.ViewSize = new Size(Math.Max(0, ViewSize.Width - 6), 1);

        _schemesLabel.Position = new Position(1, Math.Max(0, ViewSize.Height - 2));
        _schemesLabel.ViewSize = new Size(innerWidth, 1);

        _goButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _goButton.ViewSize = new Size(innerWidth, 1);

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        foreach (var hint in base.GetKeyHints())
        {
            yield return hint;
        }
        yield return ("Tab", iConsole.Localization.UiText.Get("FolderSelector.Hint.CompletePath", "Complete the path from the ones typed here and the folders visited before."));
        yield return ("Ctrl+G", iConsole.Localization.UiText.Get("FolderSelector.Hint.GoAnywhere", "Go, from anywhere on this page."));
        yield return ("Type", GetSchemesHint());
    }
}
