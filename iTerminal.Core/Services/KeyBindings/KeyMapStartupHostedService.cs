using iConsole.Commands;
using iConsole.Data;
using System.Reflection;
using iConsole.Localization;
using iConsole.Services;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace iTerminal.Core.Services.KeyBindings;

/// <summary>Where the user's key map is read from: the data folder, next to the database.</summary>
public sealed record KeyMapLocation(string Path);

/// <summary>
/// Reads <c>keymap.json</c> once, at start-up, and makes it the key map before the first key is read. A file that
/// cannot be read leaves the default keys in place. Bindings that were not used are logged with the reason, and a
/// notification names them. Edits take effect at the next start.
/// </summary>
public sealed class KeyMapStartupHostedService(KeyMapLocation location, ILogger logger, INotificationService? notifications = null) : IHostedService
{
    private readonly ILogger _logger = logger.ForContext<KeyMapStartupHostedService>();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The map is set on every start, even when there is no file, so a map left by an earlier start never survives.
        if (!File.Exists(location.Path))
        {
            KeyMap.Apply(new Dictionary<string, Key?>());
            return;
        }

        KeyMapParseResult result;
        try
        {
            string json = await File.ReadAllTextAsync(location.Path, cancellationToken);
            result = KeyMapFile.Parse(json, CommandTypes());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.Warning(exception, "The key map {Path} could not be read; the default keys are used", location.Path);
            KeyMap.Apply(new Dictionary<string, Key?>());
            notifications?.Notify(
                UiText.Get("KeyMap.Title", "Key map"),
                UiText.Get("KeyMap.Notify.Unreadable", "keymap.json could not be read, so the default keys are used. The log says why."));
            return;
        }

        KeyMap.Apply(result.Bindings);
        _logger.Information("{Count} key binding(s) applied from {Path}", result.Bindings.Count, location.Path);

        if (result.Problems.Count == 0)
        {
            return;
        }

        foreach (KeyBindingProblem problem in result.Problems)
        {
            _logger.Warning("Key binding {Binding} not used: {Reason}", problem.Binding, problem.Reason);
        }

        notifications?.Notify(
            UiText.Get("KeyMap.Title", "Key map"),
            string.Format(
                UiText.Get("KeyMap.Notify.Problems", "{0} key binding(s) in keymap.json were not used: {1}. The log says why."),
                result.Problems.Count,
                string.Join(", ", result.Problems.Select(problem => problem.Binding))));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Every concrete command in the app's own assemblies: the command base and the application's commands.</summary>
    /// <summary>The types that load. One whose optional dependency is missing must not stop the start-up.</summary>
    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static IEnumerable<Type> CommandTypes() =>
        new[] { typeof(Command).Assembly, typeof(KeyMapFile).Assembly }
            .Distinct()
            .SelectMany(LoadableTypes)
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(Command).IsAssignableFrom(type));
}
