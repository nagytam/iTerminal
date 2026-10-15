using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls.StatusBar;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Services;

namespace iConsole.Dialogs;

public class FileViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    public Url? Url { get; set; } = null;
    private readonly IContentModelService _contentModelService;
    private readonly IFileToTextConverterService _fileToTextConverterService;
    private readonly TextViewerControl _textViewerControl;
    private readonly ViewerStatusBar _statusBar;

    public FileViewerDialog(
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(FileViewerDialog), themeService)
    {
        _contentModelService = contentModelService;
        _fileToTextConverterService = fileToTextConverterService;
        _textViewerControl = new TextViewerControl(false, themeService);
        _statusBar = new ViewerStatusBar(
            _textViewerControl.ScrollableModel,
            _textViewerControl.SearchModel,
            _textViewerControl.TextModel,
            beepService,
            dateTimeProviderService,
            themeService);
        AddControl(_textViewerControl);
        AddControl(_statusBar);
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton("Close", CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Escape
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            Close();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (HitTestTitleBarClose(mouseEvent))
        {
            Close();
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>
    /// Hit-test the close label at the right end of the title row (see <see cref="PopUpWindow.Draw"/> + <see cref="StringHelper.AlignCenter"/>).
    /// </summary>
    private bool HitTestTitleBarClose(MouseEvent mouseEvent)
    {
        if (!mouseEvent.LeftClicked || mouseEvent.IsRelease || !IsVisible)
        {
            return false;
        }

        var actions = GetActionButtons();
        if (actions.Length == 0 || ViewSize.Width < 3)
        {
            return false;
        }

        if (mouseEvent.Position.Y != AbsolutePosition.Y)
        {
            return false;
        }

        var innerW = (int)ViewSize.Width - 2;
        var lineLen = (int)ViewSize.Width;
        var start = (lineLen - innerW) / 2;
        var closeLeft = AbsolutePosition.X + start + (innerW - actions.Length);
        var closeRight = AbsolutePosition.X + start + innerW - 1;
        return mouseEvent.Position.X >= closeLeft && mouseEvent.Position.X <= closeRight;
    }

    private sealed class CloseSelfCommand(FileViewerDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.Close();
            return Task.CompletedTask;
        }
    }

    public override async Task OnInitializeAsync()
    {
        var fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
        await _fileToTextConverterService.ReadFileAsync(Url, fileSystem!, _textViewerControl.TextModel);

        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 4 - CloseActionView.Length)) ?? "Viewer";
        }

        _textViewerControl.Position = new(1, 1);
        _textViewerControl.ViewSize =
            new(ViewSize.Width - 2, ViewSize.Height - 3);

        _statusBar.Position = new Position(1, ViewSize.Height - 2);
        _statusBar.ViewSize = new(ViewSize.Width - 3, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
