namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>
/// Reads the table <c>virsh list --all</c> prints: a header, a dashed rule, then one row per domain
/// as <c>Id Name State</c>, where Id is a number or <c>-</c> for a domain that is not running.
/// The state is matched from the end of the row, so a name with spaces in it still parses.
/// </summary>
public static class VirshListParser
{
    private static readonly (string Text, VirtualMachineState State)[] KnownStates =
    [
        ("running", VirtualMachineState.Running),
        ("idle", VirtualMachineState.Idle),
        ("paused", VirtualMachineState.Paused),
        ("blocked", VirtualMachineState.Blocked),
        ("in shutdown", VirtualMachineState.InShutdown),
        ("shut off", VirtualMachineState.ShutOff),
        ("crashed", VirtualMachineState.Crashed),
        ("pmsuspended", VirtualMachineState.PowerManagementSuspended),
    ];

    public static IReadOnlyList<VirtualMachine> Parse(string output)
    {
        List<VirtualMachine> machines = [];
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.All(c => c == '-') || line.StartsWith("Id ", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !(fields[0] == "-" || fields[0].All(char.IsDigit)))
            {
                continue;
            }

            var rest = fields[1].Trim();
            var known = KnownStates.FirstOrDefault(state => rest.EndsWith(state.Text, StringComparison.OrdinalIgnoreCase));
            if (known.Text is not null)
            {
                var name = rest[..^known.Text.Length].Trim();
                if (name.Length > 0)
                {
                    machines.Add(new VirtualMachine(name, known.State, known.Text));
                }
            }
            else if (rest.Length > 0)
            {
                machines.Add(new VirtualMachine(rest, VirtualMachineState.Unknown, "unknown"));
            }
        }

        return machines;
    }
}
