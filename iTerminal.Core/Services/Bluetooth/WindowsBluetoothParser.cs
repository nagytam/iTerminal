using System.Text.Json;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.Bluetooth;

/// <summary>
/// Reads what the Windows listing script prints: <c>ConvertTo-Json</c> of one object per Bluetooth device node
/// (<c>InstanceId</c>, <c>Name</c>, <c>Present</c>, <c>Connected</c>). Kept apart from the process call so the
/// format is tested against recorded output, as <see cref="BluetoothctlParser"/> is for BlueZ.
/// </summary>
public static partial class WindowsBluetoothParser
{
    /// <summary>
    /// The script <see cref="BluetoothService"/> runs on Windows. <c>Get-PnpDevice -Class Bluetooth</c> returns one node
    /// per device and one per service it offers; only the device nodes (<c>BTHENUM\DEV_&lt;12 hex digits&gt;\…</c> for
    /// classic devices, <c>BTHLE\DEV_…</c> for low energy ones) are kept. The output is forced to UTF-8, which is how the
    /// process runner reads it, so a device name outside the console's code page survives; a machine with no Bluetooth
    /// class at all prints nothing instead of an error. The connected flag is the
    /// <c>System.Devices.Connected</c> property; a device that does not report it reads as not connected.
    /// </summary>
    public const string ListScript =
        "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
        "Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | " +
        "Where-Object { $_.InstanceId -match '^BTH(ENUM|LE)\\\\DEV_[0-9A-F]{12}\\\\' } | " +
        "ForEach-Object { " +
        "$c = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName '{83DA6326-97A6-4088-9453-A1923F573B29} 15' -ErrorAction SilentlyContinue).Data; " +
        "[pscustomobject]@{ InstanceId = $_.InstanceId; Name = $_.FriendlyName; Present = $_.Present; Connected = [bool]$c } " +
        "} | ConvertTo-Json -Compress";

    [GeneratedRegex(@"^BTH(?:ENUM|LE)\\DEV_(?<address>[0-9A-Fa-f]{12})\\", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceNode();

    /// <summary>
    /// The devices in the script's output. PowerShell prints a bare object, not an array, when there is one result, and
    /// nothing at all when there is none; both are read. A node of a device that is not present is dropped, and a device
    /// that shows up as both a classic and a low-energy node is listed once. Windows keeps a node only for a device that
    /// was paired, so every device read here is paired. Returns false when the text is not the expected JSON.
    /// </summary>
    public static bool TryParseDevices(string standardOutput, out IReadOnlyList<BluetoothDevice> devices)
    {
        devices = [];
        var text = standardOutput.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            List<BluetoothDevice> result = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            IEnumerable<JsonElement> nodes = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray(),
                JsonValueKind.Object => [document.RootElement],
                _ => throw new JsonException("Neither an object nor an array."),
            };
            foreach (JsonElement node in nodes)
            {
                if (node.ValueKind != JsonValueKind.Object
                    || Text(node, "InstanceId") is not { } instanceId
                    || DeviceNode().Match(instanceId) is not { Success: true } match
                    || Flag(node, "Present") == false)
                {
                    continue;
                }

                string address = FormatAddress(match.Groups["address"].Value);
                if (!seen.Add(address))
                {
                    continue;
                }

                string name = Text(node, "Name") is { Length: > 0 } friendly ? friendly : address;
                result.Add(new BluetoothDevice(address, name, IsPaired: true, IsConnected: Flag(node, "Connected") == true));
            }

            devices = result;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Twelve hex digits as <c>AA:BB:CC:DD:EE:FF</c>, the form the rest of the Bluetooth code uses.</summary>
    private static string FormatAddress(string digits)
    {
        string upper = digits.ToUpperInvariant();
        return string.Join(':', Enumerable.Range(0, 6).Select(index => upper.Substring(index * 2, 2)));
    }

    private static string? Text(JsonElement node, string property) =>
        node.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool? Flag(JsonElement node, string property) =>
        node.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
