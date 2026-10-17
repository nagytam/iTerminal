using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace iTerminal.Core.Services.SystemSettings;

/// <summary>Where one scope's environment variables are kept, whatever the system calls that place.</summary>
public interface IEnvironmentVariableStore
{
    IReadOnlyList<SystemEnvironmentVariable> Read();

    void Set(string name, string value);

    void Remove(string name);
}

/// <summary>
/// The Windows environment: <c>HKCU\Environment</c> for the user, the Session Manager's
/// <c>Environment</c> key for the machine. It is written through the registry rather than
/// <see cref="Environment.SetEnvironmentVariable(string, string?, EnvironmentVariableTarget)"/>
/// because that writes every value as plain text — a <c>Path</c> that was an expandable string
/// (<c>%SystemRoot%\system32</c>) stops expanding, and half the machine's tools stop being found.
/// Here the value keeps its kind, and a new value with a <c>%NAME%</c> in it becomes expandable.
/// Windows is then told (<c>WM_SETTINGCHANGE</c>), so new programs started from Explorer see the change.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryEnvironmentStore(EnvironmentScope scope) : IEnvironmentVariableStore
{
    internal const string MachineKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    internal const string UserKeyPath = "Environment";

    private RegistryKey? Open(bool writable) => scope == EnvironmentScope.User
        ? Registry.CurrentUser.OpenSubKey(UserKeyPath, writable)
        : Registry.LocalMachine.OpenSubKey(MachineKeyPath, writable);

    public IReadOnlyList<SystemEnvironmentVariable> Read()
    {
        using var key = Open(writable: false);
        if (key is null)
        {
            return [];
        }

        var variables = new List<SystemEnvironmentVariable>();
        foreach (var name in key.GetValueNames())
        {
            if (name.Length == 0 || key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value)
            {
                continue;
            }

            variables.Add(new SystemEnvironmentVariable(name, value));
        }

        return [.. variables.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public void Set(string name, string value)
    {
        using var key = Open(writable: true) ?? throw new IOException("The environment key could not be opened.");
        var existing = key.GetValueNames().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        var kind = ChooseKind(existing is null ? null : key.GetValueKind(existing), value);
        if (existing is not null && existing != name)
        {
            key.DeleteValue(existing, throwOnMissingValue: false);
        }

        key.SetValue(name, value, kind);
        Broadcast();
    }

    public void Remove(string name)
    {
        using var key = Open(writable: true) ?? throw new IOException("The environment key could not be opened.");
        var existing = key.GetValueNames().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            key.DeleteValue(existing, throwOnMissingValue: false);
        }

        Broadcast();
    }

    /// <summary>An expandable value stays expandable; a plain one becomes so only by naming a variable.</summary>
    internal static RegistryValueKind ChooseKind(RegistryValueKind? existing, string value)
    {
        var hasReference = value.Contains('%') && value.IndexOf('%') != value.LastIndexOf('%');
        return existing == RegistryValueKind.ExpandString || hasReference
            ? RegistryValueKind.ExpandString
            : RegistryValueKind.String;
    }

    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    private static void Broadcast()
    {
        try
        {
            // A window that hangs would hold this for as long as it likes; five seconds is the limit.
            SendMessageTimeout(HwndBroadcast, WmSettingChange, UIntPtr.Zero, "Environment", SmtoAbortIfHung, 5000, out _);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Programs started later still read the registry; only the running ones miss the news.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}

/// <summary>
/// A <c>NAME=value</c> file, the format <c>/etc/environment</c> and systemd's <c>environment.d</c>
/// share. Only the line of the variable being changed is touched: comments, blank lines and the
/// entries of other tools stay where they were, so editing the machine's file does not rewrite
/// what the distribution put there.
/// </summary>
public sealed class EnvironmentFileStore(string path) : IEnvironmentVariableStore
{
    public string Path { get; } = path;

    public IReadOnlyList<SystemEnvironmentVariable> Read()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        var variables = new List<SystemEnvironmentVariable>();
        foreach (var line in File.ReadAllLines(Path))
        {
            if (TryParse(line, out var name, out var value))
            {
                variables.RemoveAll(v => v.Name == name);
                variables.Add(new SystemEnvironmentVariable(name, value));
            }
        }

        return [.. variables.OrderBy(v => v.Name, StringComparer.Ordinal)];
    }

    public void Set(string name, string value)
    {
        var lines = ReadLines();
        var entry = Format(name, value);
        var index = lines.FindIndex(line => TryParse(line, out var existing, out _) && existing == name);
        if (index >= 0)
        {
            lines[index] = entry;
            lines.RemoveAll(line => !ReferenceEquals(line, entry) && TryParse(line, out var duplicate, out _) && duplicate == name);
        }
        else
        {
            lines.Add(entry);
        }

        Write(lines);
    }

    public void Remove(string name)
    {
        var lines = ReadLines();
        if (lines.RemoveAll(line => TryParse(line, out var existing, out _) && existing == name) > 0)
        {
            Write(lines);
        }
    }

    private List<string> ReadLines() => File.Exists(Path) ? [.. File.ReadAllLines(Path)] : [];

    private void Write(List<string> lines)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Whole file or nothing: a login reads this at any moment, and half a file is a login without a PATH.
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, string.Concat(lines.Select(line => line + "\n")), new UTF8Encoding(false));
        File.Move(temporary, Path, overwrite: true);
    }

    /// <summary>Reads one <c>NAME=value</c> line; blank lines, comments and anything else are not variables.</summary>
    internal static bool TryParse(string line, out string name, out string value)
    {
        name = value = string.Empty;
        var text = line.Trim();
        if (text.Length == 0 || text[0] == '#')
        {
            return false;
        }

        if (text.StartsWith("export ", StringComparison.Ordinal))
        {
            text = text[7..].TrimStart();
        }

        var equals = text.IndexOf('=');
        if (equals <= 0)
        {
            return false;
        }

        name = text[..equals].TrimEnd();
        value = Unquote(text[(equals + 1)..].Trim());
        return name.Length > 0;
    }

    private static string Unquote(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            var builder = new StringBuilder();
            for (var i = 1; i < raw.Length - 1; i++)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length - 1)
                {
                    i++;
                }

                builder.Append(raw[i]);
            }

            return builder.ToString();
        }

        return raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'' ? raw[1..^1] : raw;
    }

    internal static string Format(string name, string value)
    {
        var needsQuotes = value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || c is '#' or '"' or '\'' or '\\');
        return needsQuotes
            ? $"{name}=\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : $"{name}={value}";
    }
}
