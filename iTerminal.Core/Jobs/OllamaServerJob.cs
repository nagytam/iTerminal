using iConsole.Jobs;
using iTerminal.Core.AI;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Keeps the local Ollama server the AI prompt bar uses running while the application runs. When nothing answers
/// at the configured address and Ollama is installed, the server is started from the data folder and stopped again
/// on shutdown; a server that was already running is used and left alone. The AI bar is checked again whenever the
/// server becomes reachable or stops answering, because it is checked once when the bar starts and a server that
/// comes up later would otherwise leave it saying "unreachable" for the rest of the session.
/// </summary>
public class OllamaServerJob(
    OllamaServer server,
    IEnumerable<IRuntimeDependency> runtimeDependencies,
    IAiChatService chatService,
    AiChatModel model,
    Serilog.ILogger logger) : Job
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        OllamaRuntimeDependency? dependency = runtimeDependencies.OfType<OllamaRuntimeDependency>().FirstOrDefault();
        if (dependency is null || !server.IsLocal)
        {
            return;
        }

        bool? wasReachable = null;
        DateTime retryAt = DateTime.MinValue;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool reachable = await server.IsReachableAsync(cancellationToken);
                if (!reachable
                    && DateTime.UtcNow >= retryAt
                    && dependency.BinaryPath() is { } binary
                    && dependency.InstalledVersion() is not null)
                {
                    try
                    {
                        // Returns once the server answers, so it is reachable from here on.
                        await server.EnsureRunningAsync(binary, dependency.ModelsDirectoryFor(binary), cancellationToken);
                        reachable = true;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // Not retried for a minute: a program that fails to start would otherwise be started every few seconds.
                        retryAt = DateTime.UtcNow + RetryAfterFailure;
                        logger.Warning(exception, "The Ollama server could not be started");
                    }
                }

                if (reachable != wasReachable)
                {
                    wasReachable = reachable;
                    await chatService.CheckAvailabilityAsync(model);
                }

                await Task.Delay(PollInterval, cancellationToken);

                // A window in the background does not need the server watched; it is checked again when focus returns.
                await iConsole.Services.TerminalFocus.Current.WaitForFocusAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        finally
        {
            server.Stop();
        }
    }
}
