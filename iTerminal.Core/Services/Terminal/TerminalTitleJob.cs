using iConsole.Interfaces;
using iConsole.Jobs;
using iConsole.Services;

namespace iTerminal.Core.Services.Terminal;

/// <summary>
/// Keeps the terminal's title on the folder of the panel the person is working in ("C:\Users\me - iTerminal"), so a tab
/// strip or a taskbar button says where the application is. It looks twice a second and writes only when the title has
/// changed. When the application stops, the terminal gets back the title it had.
/// </summary>
public sealed class TerminalTitleJob(IContentModelService contentModelService, ITerminalTitleService terminalTitle) : Job
{
    public const string ApplicationName = "iTerminal";

    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    /// <summary>The title for the panel that has the focus (or had it last), or the application's name when there is none.</summary>
    public static string BuildTitle(IContentModel? model)
    {
        string location = model?.Url.FullUrl ?? string.Empty;
        return location.Length == 0 ? ApplicationName : location + " - " + ApplicationName;
    }

    /// <summary>Brings the title in line with the panels. Public so a test can do in one call what a poll does.</summary>
    public void Publish()
    {
        terminalTitle.Title = BuildTitle(contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel());
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Publish();
                }
                catch (Exception exception)
                {
                    // A failing title must not stop the job: the next poll tries again.
                    System.Diagnostics.Debug.WriteLine($"Terminal title: {exception.Message}");
                }

                await Task.Delay(Interval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // Leave the tab as it was found.
            terminalTitle.Restore();
        }
    }
}
