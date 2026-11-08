using iConsole.Commands;
using iConsole.Dialogs;
using iConsole.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Core.Commands;

/// <summary>
/// Where a command that shows a dialog gets a new one from when the language has changed since it was built. The dialogs say their
/// words as they are constructed, and the commands are made at start-up, so without this a dialog opened after a language switch
/// would still speak the old language until the next run. Set once by <see cref="DialogRefreshHostedService"/>.
/// </summary>
public static class DialogRefresh
{
    private static volatile IServiceProvider? _services;

    public static IServiceProvider? Services
    {
        get => _services;
        set => _services = value;
    }

    /// <summary>The language the interface speaks now.</summary>
    public static string Language => UiText.Current?.LanguageCode ?? "en";
}

/// <summary>Hands the application's services to <see cref="DialogRefresh"/> once they exist.</summary>
public sealed class DialogRefreshHostedService(IServiceProvider services) : Microsoft.Extensions.Hosting.IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        DialogRefresh.Services = services;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Base for commands whose action is opening a pre-built dialog. Subclasses declare the key,
/// title, and description; override <see cref="OnBeforeShow"/> to seed the dialog from current
/// state, and <see cref="Command.CanExecute"/> to gate availability. When the language has changed since the dialog was built the
/// command builds a fresh one first, so the dialog speaks the new language.
/// </summary>
public abstract class ShowDialogCommand<TDialog>(TDialog dialog) : Command where TDialog : Dialog
{
    private TDialog _dialog = dialog;
    private string _language = DialogRefresh.Language;

    protected TDialog Dialog => _dialog;

    /// <summary>Called right before the dialog is shown.</summary>
    protected virtual void OnBeforeShow(TDialog dialog)
    {
    }

    /// <summary>Replaces the dialog with a freshly built one when the language is not the one it was built in.</summary>
    internal bool RefreshDialogForLanguage()
    {
        string language = DialogRefresh.Language;
        if (string.Equals(language, _language, StringComparison.Ordinal) || DialogRefresh.Services is not { } services)
        {
            return false;
        }

        _language = language;
        if (services.GetService<TDialog>() is { } fresh)
        {
            _dialog = fresh;
            return true;
        }

        return false;
    }

    public override async Task ExecuteAsync(object? args)
    {
        if (Control is null)
        {
            return;
        }

        RefreshDialogForLanguage();
        OnBeforeShow(_dialog);
        await Control.ShowDialogAsync(_dialog);
    }
}