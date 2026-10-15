using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Shell;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+O: hands the whole terminal to a real, interactive shell where the focused panel is — the
/// user's shell in a local folder, <c>docker exec -it</c> inside a container, <c>ssh -t</c> on an SSH
/// drive (<see cref="IShellLaunchSource"/>; anything else gets a local shell in the home folder).
/// <c>exit</c> brings the panels back, re-read, since whatever the shell did to them is unknown.
///
/// <para>The key handler only starts the session: it lasts as long as the user likes, and the
/// handover runs on a thread of its own (<see cref="IInteractiveShellService"/>).</para>
/// </summary>
public class InteractiveShellCommand(
    IContentModelService contentModelService,
    IInteractiveShellService shellService,
    ISettingsService? settingsService = null) : Command
{
    public override string Title => "Shell";
    public override string Description => "Open an interactive shell where the focused panel is — a local folder, a Docker container, an SSH host; exit comes back.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.O, false, false, true);

    /// <summary>The session started last, including the panels' refresh after it. Nothing waits on it; it exists so a test can.</summary>
    public Task SessionTask { get; private set; } = Task.CompletedTask;

    public override bool CanExecute() => !shellService.IsRunning;

    /// <summary>Where the shell for the panel that has (or last had) focus runs.</summary>
    public ShellLaunch ChooseLaunch()
    {
        var model = contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();
        var configuredShell = settingsService?.Get(ShellLaunch.ShellSettingKey);
        return (model as IShellLaunchSource)?.GetShellLaunch(configuredShell)
               ?? ShellLaunch.Local(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), configuredShell);
    }

    public override Task ExecuteAsync(object? args)
    {
        var launch = ChooseLaunch();
        SessionTask = Task.Run(async () =>
        {
            await shellService.RunAsync(launch);

            // Only the local drive notices its own changes; a file made in the container or over
            // ssh would otherwise not be listed until something else refreshed the panel.
            foreach (var model in contentModelService.ContentModels.ToList())
            {
                model.Refresh(forceRefresh: true);
            }
        });

        return Task.CompletedTask;
    }
}
