using System.Text.Json;
using iConsole.Commands;
using iConsole.Data;

namespace iTerminal.Core.Services.KeyBindings;

/// <summary>A binding in <c>keymap.json</c> that was not used, and why.</summary>
public sealed record KeyBindingProblem(string Binding, string Reason);

/// <summary>The bindings that were accepted, and the ones that were not.</summary>
public sealed record KeyMapParseResult(IReadOnlyDictionary<string, Key?> Bindings, IReadOnlyList<KeyBindingProblem> Problems);

/// <summary>
/// Reads the user's key map: a JSON object with a <c>bindings</c> object that maps a command to a key, for example
/// <c>{ "bindings": { "CopyCommand": "Ctrl+F5", "ToggleHiddenFilesCommand": "none" } }</c>.
/// <list type="bullet">
/// <item>A command is named by its type name, or by its full type name when two commands share the short one.</item>
/// <item>The key is written as the help screen prints it (<see cref="KeyBindingText"/>); <c>none</c> removes the key.</item>
/// <item>A key that <see cref="KeyBindingRules"/> refuses is not used, and nor is a key already given to an earlier binding in the file.</item>
/// <item>A binding that cannot be used is reported and skipped; the others still apply.</item>
/// </list>
/// </summary>
public static class KeyMapFile
{
    public const string FileName = "keymap.json";

    /// <summary>The value that removes a command's key.</summary>
    public const string NoKey = "none";

    /// <summary>Parses <paramref name="json"/>. Throws <see cref="JsonException"/> if the file is not a key map at all.</summary>
    public static KeyMapParseResult Parse(string json, IEnumerable<Type> commandTypes)
    {
        List<Type> types = commandTypes.ToList();
        var bindings = new Dictionary<string, Key?>(StringComparer.Ordinal);
        var problems = new List<KeyBindingProblem>();
        var boundKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("bindings", out JsonElement section)
            || section.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("the file must hold a \"bindings\" object");
        }

        foreach (JsonProperty binding in section.EnumerateObject())
        {
            Bind(binding, types, bindings, boundKeys, problems);
        }

        return new KeyMapParseResult(bindings, problems);
    }

    private static void Bind(
        JsonProperty binding,
        List<Type> types,
        Dictionary<string, Key?> bindings,
        Dictionary<string, string> boundKeys,
        List<KeyBindingProblem> problems)
    {
        string name = binding.Name;
        if (binding.Value.ValueKind != JsonValueKind.String)
        {
            problems.Add(new KeyBindingProblem(name, "the key must be written as text"));
            return;
        }

        Type? command = FindCommand(name, types, out string? ambiguity);
        if (command is null)
        {
            problems.Add(new KeyBindingProblem(name, ambiguity ?? "no command has this name"));
            return;
        }

        string commandName = command.FullName ?? command.Name;
        if (bindings.ContainsKey(commandName))
        {
            problems.Add(new KeyBindingProblem(name, "this command is bound more than once; the first binding is used"));
            return;
        }

        string text = binding.Value.GetString() ?? string.Empty;
        if (string.Equals(text.Trim(), NoKey, StringComparison.OrdinalIgnoreCase))
        {
            bindings[commandName] = null;
            return;
        }

        if (!KeyBindingText.TryParse(text, out Key? key, out string? error) || key is null)
        {
            problems.Add(new KeyBindingProblem(name, error ?? "not a key"));
            return;
        }

        if (KeyBindingRules.Refusal(key) is { } refusal)
        {
            problems.Add(new KeyBindingProblem(name, $"{Command.FormatKey(key)} may not be bound: {refusal}"));
            return;
        }

        string printed = Command.FormatKey(key);
        if (boundKeys.TryGetValue(printed, out string? other))
        {
            problems.Add(new KeyBindingProblem(name, $"{printed} is already bound to {other} in this file"));
            return;
        }

        boundKeys[printed] = name;
        bindings[commandName] = key;
    }

    private static Type? FindCommand(string name, List<Type> types, out string? ambiguity)
    {
        ambiguity = null;
        List<Type> matches = types.Where(type => type.FullName == name).ToList();
        if (matches.Count == 0)
        {
            matches = types.Where(type => type.Name == name).ToList();
        }

        if (matches.Count > 1)
        {
            ambiguity = "more than one command has this name; use the full name: "
                + string.Join(", ", matches.Select(type => type.FullName));
            return null;
        }

        return matches.FirstOrDefault();
    }
}
