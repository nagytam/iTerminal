namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>
/// Reads <c>VBoxManage list --long vms</c>: one block per machine, with a <c>Name:</c> line and a <c>State:</c> line
/// among the other <c>Key: value</c> lines. A state reads <c>running (since …)</c> or <c>powered off</c>; only the
/// words before the parenthesis are matched, so the time after it never matters.
/// </summary>
public static class VirtualBoxListParser
{
    private static readonly (string Text, VirtualMachineState State)[] KnownStates =
    [
        ("running", VirtualMachineState.Running),
        ("paused", VirtualMachineState.Paused),
        ("powered off", VirtualMachineState.ShutOff),
        ("saved", VirtualMachineState.ShutOff),
        ("aborted", VirtualMachineState.Crashed),
        ("stopping", VirtualMachineState.InShutdown),
    ];

    public static IReadOnlyList<VirtualMachine> Parse(string output)
    {
        List<VirtualMachine> machines = [];
        string? name = null;
        string? stateText = null;

        void Flush()
        {
            if (name is null)
            {
                return;
            }

            if (stateText is null)
            {
                machines.Add(new VirtualMachine(name, VirtualMachineState.Unknown, "unknown"));
                return;
            }

            var known = KnownStates.FirstOrDefault(candidate => string.Equals(candidate.Text, stateText, StringComparison.OrdinalIgnoreCase));
            machines.Add(known.Text is null
                ? new VirtualMachine(name, VirtualMachineState.Unknown, stateText)
                : new VirtualMachine(name, known.State, stateText));
        }

        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith("Name:", StringComparison.Ordinal))
            {
                Flush();
                name = line["Name:".Length..].Trim();
                stateText = null;
                if (name.Length == 0)
                {
                    name = null;
                }
            }
            else if (line.StartsWith("State:", StringComparison.Ordinal) && name is not null)
            {
                var value = line["State:".Length..].Trim();
                var parenthesis = value.IndexOf(" (", StringComparison.Ordinal);
                stateText = parenthesis >= 0 ? value[..parenthesis] : value;
            }
        }

        Flush();
        return machines;
    }
}
