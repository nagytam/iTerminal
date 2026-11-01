using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Bluetooth;

/// <summary>
/// Reads what <c>bluetoothctl</c> prints. Kept apart from the process calls so the formats can be tested
/// against recorded output, and so a change in one BlueZ release is caught by a test, not by a user.
/// </summary>
public static class BluetoothctlParser
{
    /// <summary>
    /// Lines of <c>bluetoothctl devices</c>: <c>Device AA:BB:CC:DD:EE:FF Name With Spaces</c>. A device that
    /// has no name yet is shown by its address, as BlueZ does.
    /// </summary>
    public static IReadOnlyList<(string Address, string Name)> ParseDevices(string standardOutput)
    {
        List<(string Address, string Name)> devices = [];
        foreach (var line in Lines(standardOutput))
        {
            if (!line.StartsWith("Device ", StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line["Device ".Length..];
            var address = rest.Length >= 17 ? rest[..17] : string.Empty;
            if (!IsAddress(address))
            {
                continue;
            }

            var name = rest.Length > 18 ? rest[18..].Trim() : string.Empty;
            devices.Add((address, name.Length == 0 ? address : name));
        }

        return devices;
    }

    /// <summary>
    /// What <c>bluetoothctl info &lt;address&gt;</c> says about one device: its paired and connected state.
    /// An absent or unreadable answer reads as neither.
    /// </summary>
    public static (bool IsPaired, bool IsConnected) ParseState(string standardOutput)
    {
        var isPaired = false;
        var isConnected = false;
        foreach (var line in Lines(standardOutput))
        {
            if (line.StartsWith("Paired:", StringComparison.Ordinal))
            {
                isPaired = IsYes(line["Paired:".Length..]);
            }
            else if (line.StartsWith("Connected:", StringComparison.Ordinal))
            {
                isConnected = IsYes(line["Connected:".Length..]);
            }
        }

        return (isPaired, isConnected);
    }

    /// <summary>
    /// True when <c>bluetoothctl devices</c> could not list anything: it failed to run, or there is no adapter.
    /// The device lines themselves are not searched, so a device named after a failure does not count.
    /// </summary>
    public static bool ListingFailed(ProcessOutput output) =>
        !output.Success || Lines(output.StandardOutput).Any(line => line.Contains("No default controller", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when a command did not do what it was asked. bluetoothctl often exits 0 after a refused connect,
    /// so the text is read as well as the exit code.
    /// </summary>
    public static bool Failed(ProcessOutput output) =>
        !output.Success || Lines(output.StandardOutput + "\n" + output.StandardError).Any(IsFailureLine);

    /// <summary>The line that says why, or the first line of the error text when no line names a failure.</summary>
    public static string Reason(ProcessOutput output) =>
        Lines(output.StandardOutput + "\n" + output.StandardError).FirstOrDefault(IsFailureLine) ?? output.FirstErrorLine;

    /// <summary>True for a six-byte address written <c>AA:BB:CC:DD:EE:FF</c>, the only form that is passed to the tool.</summary>
    public static bool IsAddress(string value)
    {
        if (value.Length != 17)
        {
            return false;
        }

        for (var index = 0; index < 17; index++)
        {
            var character = value[index];
            var expectColon = index % 3 == 2;
            if (expectColon ? character != ':' : !Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFailureLine(string line) =>
        line.Contains("Failed to", StringComparison.OrdinalIgnoreCase)
        || line.Contains("org.bluez.Error", StringComparison.Ordinal)
        || line.Contains("No default controller", StringComparison.OrdinalIgnoreCase)
        || IsUnknownDevice(line);

    /// <summary>
    /// BlueZ's answer to an address it does not know: <c>Device AA:BB:CC:DD:EE:FF not available</c>. Only that exact
    /// shape counts, so a device named "Speaker (not available)" in a listing is not taken for a failure.
    /// </summary>
    private static bool IsUnknownDevice(string line)
    {
        var parts = line.Split(' ', 3);
        return parts.Length == 3
            && (parts[0] == "Device" || parts[0] == "DeviceSet")
            && IsAddress(parts[1])
            && parts[2] == "not available";
    }

    private static bool IsYes(string value) => value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
