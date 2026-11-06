using System.Text.Json;

namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>
/// Reads what the Hyper-V listing script prints: a JSON array (or, for one machine, an object) of
/// <c>{ "Name": …, "State": … }</c>, where State is Hyper-V's own word for it (<c>Running</c>, <c>Off</c>,
/// <c>Saved</c>, <c>Paused</c>…). The word is kept as the state text; the enum only decides which actions apply.
/// </summary>
public static class HyperVListParser
{
    public static IReadOnlyList<VirtualMachine> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            var root = document.RootElement;
            IEnumerable<JsonElement> elements = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray().ToList(),
                JsonValueKind.Object => [root],
                _ => [],
            };

            List<VirtualMachine> machines = [];
            foreach (var element in elements.Where(e => e.ValueKind == JsonValueKind.Object))
            {
                string name = Text(element, "Name");
                if (name.Length == 0)
                {
                    continue;
                }

                string state = Text(element, "State");
                machines.Add(new VirtualMachine(name, StateOf(state), state.Length > 0 ? state : "unknown"));
            }

            return machines;
        }
    }

    /// <summary>
    /// A saved machine counts as shut off: Start resumes it, and nothing else applies to it. A word ending in
    /// <c>Critical</c> (the host's storage or the guest has failed) is treated like a crash: only power-off and
    /// the console are offered. The in-between words (<c>Starting</c>, <c>Resuming</c>…) are unknown, so no action is.
    /// </summary>
    public static VirtualMachineState StateOf(string word) => word switch
    {
        _ when word.EndsWith("Critical", StringComparison.OrdinalIgnoreCase) => VirtualMachineState.Crashed,
        _ when word.Equals("Running", StringComparison.OrdinalIgnoreCase) => VirtualMachineState.Running,
        _ when word.Equals("Off", StringComparison.OrdinalIgnoreCase)
            || word.Equals("Saved", StringComparison.OrdinalIgnoreCase)
            || word.Equals("FastSaved", StringComparison.OrdinalIgnoreCase) => VirtualMachineState.ShutOff,
        _ when word.Equals("Paused", StringComparison.OrdinalIgnoreCase) => VirtualMachineState.Paused,
        _ when word.Equals("Stopping", StringComparison.OrdinalIgnoreCase)
            || word.Equals("Saving", StringComparison.OrdinalIgnoreCase)
            || word.Equals("FastSaving", StringComparison.OrdinalIgnoreCase)
            || word.Equals("ForceShutdown", StringComparison.OrdinalIgnoreCase) => VirtualMachineState.InShutdown,
        _ => VirtualMachineState.Unknown,
    };

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
