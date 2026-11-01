using iConsole.Desktops;
using iConsole.Dialogs;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Core.AI;

public class UiService : IUiService
{
    private readonly IServiceProvider _serviceProvider;

    // Resolved lazily to avoid circular dependency: Content → AiPromptBar → IAiChatService → IUiService → Content
    private Content ContentWindow => _serviceProvider.GetRequiredService<Content>();
    private IContent ContentLayout => ContentWindow;

    public UiService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void ShowFilePanels() => ContentLayout.ShowFilePanels();
    public void ShowFullTerminal() => ContentLayout.ShowFullTerminal();
    public void ShowTerminalBar() => ContentLayout.ShowTerminalBar();
    public void ShowAiBar() => ContentLayout.ShowAiBar();
    public void FocusLeftPanel() => ContentLayout.FocusLeftPanel();
    public void FocusRightPanel() => ContentLayout.FocusRightPanel();

    public async Task OpenDialogAsync(string name)
    {
        Dialog? dialog = name.ToLowerInvariant() switch
        {
            "settings" => _serviceProvider.GetRequiredService<SettingsDialog>(),
            "help" => _serviceProvider.GetRequiredService<HelpDialog>(),
            "about" => _serviceProvider.GetRequiredService<AboutDialog>(),
            "disk_manager" or "disk" => _serviceProvider.GetRequiredService<DiskManagerDialog>(),
            "process_manager" or "processes" => _serviceProvider.GetRequiredService<ProcessManagerDialog>(),
            "network_manager" or "network" => _serviceProvider.GetRequiredService<NetworkManagerDialog>(),
            "system_info" or "system" => _serviceProvider.GetRequiredService<SystemInfoDialog>(),
            "task_manager" or "tasks" => _serviceProvider.GetRequiredService<TaskManagerDialog>(),
            "theme_manager" or "theme" => _serviceProvider.GetRequiredService<ThemeManagerDialog>(),
            "screen_manager" or "screens" => _serviceProvider.GetRequiredService<ScreenManagerDialog>(),
            "ai_model" or "ai_info" or "ai" => _serviceProvider.GetRequiredService<AiModelDialog>(),
            "git" or "git_client" or "source_control" => _serviceProvider.GetRequiredService<iTerminal.Core.Dialogs.Git.GitClientDialog>(),
            "currency" or "currencyconverter" or "currency converter" => _serviceProvider.GetRequiredService<iTerminal.Core.Dialogs.CurrencyConverter.CurrencyConverterDialog>(),
            "torrent" or "torrents" or "bittorrent" => _serviceProvider.GetRequiredService<iTerminal.Core.Dialogs.Torrents.TorrentManagerDialog>(),
            "encyclopedia" or "wikipedia" or "wiki" or "britannica" or "dictionary" => _serviceProvider.GetRequiredService<iTerminal.Core.Dialogs.Encyclopedia.EncyclopediaDialog>(),
            "benchmark" or "performance" or "performance_benchmark" or "speed_test" => _serviceProvider.GetRequiredService<iTerminal.Core.Dialogs.Benchmark.PerformanceBenchmarkDialog>(),
            "contacts" or "contact" or "contact_manager" or "address_book" => _serviceProvider.GetRequiredService<iTerminal.Core.Dialogs.Contacts.ContactManagerDialog>(),
            "games" or "game" or "chess" or "go" or "tictactoe" or "morris" or "snake" => _serviceProvider.GetRequiredService<iTerminal.Core.Dialogs.Games.GamesDialog>(),
            "translator" or "translate" or "translation" => _serviceProvider.GetRequiredService<iTerminal.Core.Dialogs.Translator.TranslatorDialog>(),
            _ => null
        };

        if (dialog is not null)
            await ContentWindow.ShowDialogAsync(dialog);
    }

    // Cancelling the desktop token is the same shutdown path as F10 / QuitCommand: it stops the
    // desktop loop and its jobs, and the desktop then asks the host to exit the process.
    public void Quit() =>
        _serviceProvider.GetRequiredService<IDesktop>().CancellationTokenSource.Cancel();
}
