using iConsole.Data;

namespace iConsole.Commands;

/// <summary>
/// The user's key bindings, by command type. A command's <see cref="Command.ActiveKey"/> is its own default key
/// unless the map names it: then the mapped key (or none, for <c>null</c>) is used instead.
/// <para>
/// The map is read on every key press, so it is an immutable snapshot that is swapped, never edited. The process
/// map is set once at start-up with <see cref="Apply"/>. A test that needs its own bindings holds a
/// <see cref="Scope"/>, which is ambient to its own async flow only, so parallel tests do not see each other's map.
/// </para>
/// </summary>
public static class KeyMap
{
    private static volatile IReadOnlyDictionary<string, Key?> _process = new Dictionary<string, Key?>();
    private static readonly AsyncLocal<ScopedMap?> _scoped = new();

    /// <summary>
    /// The bindings in effect: keyed by <see cref="Type.FullName"/> of the command, a value of <c>null</c> meaning
    /// the command has no key.
    /// </summary>
    public static IReadOnlyDictionary<string, Key?> Current => _scoped.Value?.Bindings ?? _process;

    /// <summary>Replaces the process-wide bindings. Called once, at start-up, before the first key is read.</summary>
    public static void Apply(IReadOnlyDictionary<string, Key?> bindings)
    {
        _process = new Dictionary<string, Key?>(bindings, StringComparer.Ordinal);
    }

    /// <summary>Makes <paramref name="bindings"/> the map for the current async flow until the returned scope is disposed.</summary>
    public static IDisposable Scope(IReadOnlyDictionary<string, Key?> bindings)
    {
        ScopedMap? previous = _scoped.Value;
        _scoped.Value = new ScopedMap(new Dictionary<string, Key?>(bindings, StringComparer.Ordinal));
        return new MapScope(previous);
    }

    /// <summary>The key <paramref name="commandType"/> responds to, given its <paramref name="defaultKey"/>.</summary>
    public static Key? Resolve(Type commandType, Key? defaultKey)
    {
        return Current.TryGetValue(commandType.FullName ?? commandType.Name, out Key? mapped) ? mapped : defaultKey;
    }

    private sealed record ScopedMap(IReadOnlyDictionary<string, Key?> Bindings);

    private sealed class MapScope(ScopedMap? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _scoped.Value = previous;
            }
        }
    }
}
