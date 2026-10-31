using iConsole.Localization;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Services;
using iConsole.Data;
using iTerminal.Core.Model;
using iConsole.Buffers;
using iTerminal.Core.Commands;
using iConsole.Controls;
using iConsole.Interfaces;
using iTerminal.Core.Model.Validations;
using iConsole.Extensions;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

public class FolderSelectorRemoteShellTabPage : FolderSelectorTabPage
{
    public TextBox TextBoxHost { get; private set; }
    private ErrorMessage _errorMessage { get; set; }
    private Button _buttonConnect { get; set; }
    private Label _hintLabel { get; set; }
    private IFrequentExecutionGuardService _frequentExecutionGuardService { get; set; }
    public string Host => TextBoxHost.Text;

    private ICommand _connectCommand { get; set; }
    protected override ICommand? SelectCommand => _connectCommand;
    protected override string SelectDescription => iConsole.Localization.UiText.Get("FolderSelector.Select.Connect", "Connect to the SSH host that has been typed in.");

    public FolderSelectorRemoteShellTabPage(
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        ISettingsService settingsService,
        IFrequentExecutionGuardService frequentExecutionGuardService,
        IThemeService themeService)
        : base("5-SSH", ConsoleKey.D5, themeService)
    {
        _frequentExecutionGuardService = frequentExecutionGuardService;

        var completionModel = new PersistedValuesCompletionModel(settingsService, "SSH-Hosts");
        TextBoxHost = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Field.SshUserHost", "SSH username@host: "),
            Placeholder = "user@host",
        }.WithAutoCompletion(completionModel);
        TextBoxHost.ValidationModel = new HostValidationModel(TextBoxHost.TextModel, dateTimeProviderService);
        _errorMessage = new ErrorMessage(TextBoxHost.ValidationModel, themeService);

        var hostValidator = (HostValidationModel)TextBoxHost.ValidationModel!;

        _connectCommand = new ConnectRemoteShellCommand(hostValidator, contentModelService);
        _buttonConnect = new Button(iConsole.Localization.UiText.Get("Button.Connect", "Connect"), _connectCommand, themeService)
        {
            HotKeyChar = iConsole.Localization.HotKeys.VisibleIn(iConsole.Localization.UiText.Get("Button.Connect", "Connect"), 'C')
        };
        _hintLabel = new Label(
            "user@host — the path defaults to /. Tab completes a host connected to before.",
            themeService);
        AddControl(TextBoxHost);
        AddControl(_errorMessage);
        AddControl(_hintLabel);
        AddControl(_buttonConnect);
    }

    public override void OnInitializeLayout()
    {
        // One row high — see FolderSelectorPathTabPage: a TextBox draws one line, and the completion
        // list is placed directly below its owner, so an over-tall box strands the suggestions at the
        // bottom of the screen.
        TextBoxHost.Position = new Position(1, 0);
        TextBoxHost.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);
        TextBoxHost.ScrollableModel.ScrollableSize = new Size(TextBoxHost.MaxLength, 1);
        TextBoxHost.ScrollableModel.ViewSize = new Size(Math.Max(0, TextBoxHost.EditableWidth), 1);

        _errorMessage.Position = new Position(1, 1);
        _errorMessage.ViewSize = new Size(Math.Max(0, ViewSize.Width - 6), 1);

        _hintLabel.Position = new Position(1, Math.Max(0, ViewSize.Height - 2));
        _hintLabel.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        _buttonConnect.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _buttonConnect.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        base.OnInitializeLayout();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        foreach (var hint in base.GetKeyHints())
        {
            yield return hint;
        }
        yield return ("Tab", iConsole.Localization.UiText.Get("FolderSelector.Hint.CompleteHost", "Complete the host from the ones connected to before."));
        yield return ("Ctrl+C", iConsole.Localization.UiText.Get("FolderSelector.Hint.ConnectAnywhere", "Connect, from anywhere on this page."));
    }
}

