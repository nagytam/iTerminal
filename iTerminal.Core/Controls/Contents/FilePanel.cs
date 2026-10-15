using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls.Contents;

public class FilePanel : ContentPanel
{
    private ISettingsService _settingsService { get; set; }
    public IContentModel? Model => ContentModel;
    private string _keyScrollPositionY => Name + 
        $"_{nameof(ContentModel.ScrollPosition)}.{nameof(ContentModel.ScrollPosition.Y)}";
    private string _keyCursorPositionY => Name +
        $"_{nameof(ContentModel.CursorPosition)}.{nameof(ContentModel.CursorPosition.Y)}";

    /// <summary>
    /// The row the cursor was on, by URL. Saved alongside the indexes because a folder is rarely the
    /// same list it was when the app closed, and an index on its own would then restore the cursor
    /// to a different file than the one the user left it on.
    /// </summary>
    private string _keyFocusedEntry => Name + "_FocusedEntry";

    public FilePanel(
        string name, 
        ContentBrowser contentBrowser,
        ItemInformation itemInformation, 
        IContentModelService contentModelService, 
        ISettingsService settingsService,
        IThemeService themeService)
        : base(
            settingsService,
            contentBrowser, 
            itemInformation, 
            contentModelService, 
            themeService)
    {
        // Must be set before `OnInitializeAsync` (base reads settings using `Name`).
        Name = name;
        _settingsService = settingsService ?? NullSettingsService.Instance;
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        if (ContentModel is null)
        {
            return;
        }

        ContentModel.ReadUrl();

        // Handed to the model rather than assigned here: the folder is enumerated on a background
        // task, so at this point the listing is still just the ".." row and anything set against it
        // is clamped to zero. The model holds the position until a listing arrives that can carry it.
        _ = int.TryParse(_settingsService.Get(_keyScrollPositionY), out int scrollPositionY);
        _ = int.TryParse(_settingsService.Get(_keyCursorPositionY), out int cursorPositionY);
        ContentModel.RestoreNavigation(_settingsService.Get(_keyFocusedEntry), cursorPositionY, scrollPositionY);
    }

    protected override void OnBeforeClose()
    {
        base.OnBeforeClose();

        if (ContentModel is not null)
        {
            _settingsService.Set(_keyScrollPositionY, ContentModel.ScrollPosition.Y.ToString());
            _settingsService.Set(_keyCursorPositionY, ContentModel.CursorPosition.Y.ToString());
            _settingsService.Set(_keyFocusedEntry, ContentModel.GetFocusedEntry()?.Url.FullUrl ?? string.Empty);
        }
    }
}
