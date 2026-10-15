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

    /// <summary>What has been typed, untrimmed. <see cref="GoToPathCommand"/> trims it.</summary>
    public string Path => TextBoxPath.Text;

    public FolderUrlValidationModel PathValidator { get; private set; }

    private ICommand _goToPathCommand { get; set; }
    protected override ICommand? SelectCommand => _goToPathCommand;
    protected override string SelectDescription => "Go to the typed path or URL.";

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
            Label = "Path or URL: ",
            MaxLength = 1024,
        }.WithAutoCompletion(completionModel);
        PathValidator = new FolderUrlValidationModel(TextBoxPath.TextModel, dateTimeProviderService);
        TextBoxPath.ValidationModel = PathValidator;
        _errorMessage = new ErrorMessage(PathValidator, themeService);

        _schemesLabel = new Label(GetSchemesHint(), themeService);

        _goToPathCommand = new GoToPathCommand(contentModelService, PathValidator);
        _goButton = new Button("Go", _goToPathCommand, themeService)
        {
            HotKeyChar = 'G'
        };

        AddControl(TextBoxPath);
        AddControl(_errorMessage);
        AddControl(_schemesLabel);
        AddControl(_goButton);
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
        yield return ("Tab", "Complete the path from the ones typed here and the folders visited before.");
        yield return ("Ctrl+G", "Go, from anywhere on this page.");
        yield return ("Type", GetSchemesHint());
    }
}
