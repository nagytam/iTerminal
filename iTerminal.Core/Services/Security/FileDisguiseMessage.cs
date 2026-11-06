using iConsole.Localization;
using iConsole.Security;

namespace iTerminal.Core.Services.Security;

/// <summary>The sentences the info strip and the properties dialog use to say what is off about a file.</summary>
public static class FileDisguiseMessage
{
    /// <summary>One sentence per finding, in the order: content, double extension, hidden characters.</summary>
    public static IReadOnlyList<string> Describe(string name, DisguiseFindings findings, FileKind kind)
    {
        List<string> sentences = [];
        string extension = FileDisguise.ExtensionOf(name);
        if (findings.HasFlag(DisguiseFindings.TypeMismatch))
        {
            sentences.Add(string.Format(
                UiText.Get("Disguise.TypeMismatch", "Content is {0}, but the name says .{1}"),
                FileDisguise.KindName(kind),
                FileDisguise.Mask(extension)));
        }

        if (findings.HasFlag(DisguiseFindings.DoubleExtension) && FileDisguise.DoubleExtension(name) is { } pair)
        {
            sentences.Add(string.Format(
                UiText.Get("Disguise.DoubleExtension", "Double extension: shows .{0}, runs as .{1}"),
                pair.Shown,
                pair.Real));
        }

        if (findings.HasFlag(DisguiseFindings.HiddenCharacters))
        {
            sentences.Add(UiText.Get("Disguise.HiddenCharacters", "The name has hidden or direction-changing characters"));
        }

        return sentences;
    }

    /// <summary>The info strip's one line: a warning sign and the sentences joined.</summary>
    public static string Summary(string name, DisguiseFindings findings, FileKind kind) =>
        "⚠ " + string.Join("; ", Describe(name, findings, kind));
}
