namespace iConsole.Localization;

/// <summary>
/// The language the framework's own controls speak, for the few places that build their text without being handed an
/// <see cref="ILocalizer"/>: the standard Cancel and Close buttons, a prompt's default answers. The application sets
/// <see cref="Current"/> once when it starts; until it does, and for a key no language has, the English fallback shows.
/// </summary>
public static class UiText
{
    private static ILocalizer? _current;
    private static readonly AsyncLocal<ScopedLocalizer?> _scoped = new();

    /// <summary>The localizer the application chose; null before it is set, so a control still shows its English text.</summary>
    public static ILocalizer? Current
    {
        get => _scoped.Value is { } scoped ? scoped.Localizer : Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value);
    }

    /// <summary>
    /// Speaks <paramref name="localizer"/> to the calling async flow only, until the returned scope is disposed; null
    /// speaks English for that flow even when the application chose another language. Tests use it so that several
    /// language tests can run at once, each with its own language. The application never calls it.
    /// </summary>
    public static IDisposable Scope(ILocalizer? localizer)
    {
        ScopedLocalizer? previous = _scoped.Value;
        _scoped.Value = new ScopedLocalizer(localizer);
        return new LanguageScope(previous);
    }

    public static string Get(string key, string fallback) =>
        Current is { } localizer && localizer.TryGet(key, out string value) && !string.IsNullOrEmpty(value) ? value : fallback;

    /// <summary>
    /// A command's one-sentence description in the chosen language. There are well over a hundred of them and each is
    /// written once, as an English literal in its command; the translations are found by the text itself, under
    /// <c>CmdDesc.</c> and the first eight hex digits of the SHA-256 of the English sentence, so no command carries a
    /// key. A sentence no language has comes back as it was.
    /// </summary>
    public static string Describe(string english)
    {
        if (string.IsNullOrWhiteSpace(english))
        {
            return english;
        }

        return Get(DescriptionKey(english), english);
    }

    /// <summary>The resource key of a command description: <c>CmdDesc.</c> and eight hex digits of its SHA-256.</summary>
    public static string DescriptionKey(string english) =>
        "CmdDesc." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(english.Trim())))[..8].ToLowerInvariant();

    private sealed record ScopedLocalizer(ILocalizer? Localizer);

    private sealed class LanguageScope(ScopedLocalizer? previous) : IDisposable
    {
        public void Dispose() => _scoped.Value = previous;
    }
}
