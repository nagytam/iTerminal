using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls.Contents;

public abstract class ContentPanel : Window
{
    private ISettingsService _settingsService { get; set; }
    private IContentModelService _contentModelService { get; set; }
    public IContentModel? ContentModel { get; private set; }
    private ContentBrowser? _contentBrowser { get; set; }
    private ItemInformation? _itemInformation { get; set; }

    public ContentPanel(
        ISettingsService settingsService,
        ContentBrowser contentBrowser, 
        ItemInformation itemInformation, 
        IContentModelService contentModelService,
        IThemeService themeService) 
        : base(themeService)
    {
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _contentModelService = contentModelService ?? NullContentModelService.Instance;
        _contentBrowser = contentBrowser;
        _itemInformation = itemInformation;

        CanFocus = true;

        if (contentBrowser is not null)
        {
            AddControl(contentBrowser);
        }
        if (itemInformation is not null)
        {
            AddControl(itemInformation, nameof(itemInformation));
        }
    }

    protected override void OnFocusChanged()
    {
        base.OnFocusChanged();
        if (IsFocused)
        {
            _contentModelService.LastFocusedPanel = Name;
            _contentModelService.FocusedPanel = Name;
            _contentBrowser?.SetFocus();
        }
        else
        {
            if (_contentModelService.FocusedPanel == Name)
            {
                _contentModelService.LastFocusedPanel = Name;
                _contentModelService.FocusedPanel = string.Empty;
            }
            _contentBrowser?.ResetFocus();
        }
    }

    public override async Task OnInitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException($"{GetType().Name} requires `Name` to be set before calling OnInitializeAsync().");
        }

        var urlString = _settingsService.Get(Name) ?? Directory.GetCurrentDirectory();
        var url = new Url(urlString);
        ContentModel = _contentModelService.AddContentModel(Name, url);
        if (ContentModel != null)
        {
            ContentModel.ContentBrowser = _contentBrowser;

            // Preserve model defaults (some tests rely on pre-seeded values).
            if (ViewSize.Width > 0 && ViewSize.Height > 0)
            {
                ContentModel.ViewSize = ViewSize;
            }

            // IMPORTANT: don't reset navigation (scroll/cursor) here. That should happen
            // when content is loaded/refreshed, otherwise it overwrites defaults.

            _contentBrowser.ContentModel = ContentModel;
            _itemInformation.ContentModel = ContentModel;
        }

        await base.OnInitializeAsync();
    }

    public override void OnWindowSizeChanged(object? sender, Size size)
    {
        ViewSize = size;
        OnInitializeLayout();
    }

    public override void OnInitializeLayout()
    {
        if (_contentBrowser is not null)
        {
            _contentBrowser.ViewSize = new Size(ViewSize.Width, Math.Max(0, ViewSize.Height - 1));
        }
        if (_itemInformation is not null)
        {
            _itemInformation.ViewSize = new Size(ViewSize.Width, 1);
        }
        base.OnInitializeLayout();
        if (_contentBrowser is not null && _itemInformation is not null)
        {
            Layout.StackVertical([_contentBrowser, _itemInformation]);
        }
    }
}
