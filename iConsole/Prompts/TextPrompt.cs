using System.ComponentModel;

namespace iConsole.Prompts;

/// <summary>
/// Asks for a value typed as text, converted to <typeparamref name="T"/> and checked by any rules added with
/// <see cref="Validate"/>. A string prompt takes the text as it is; other types are converted with the type's invariant
/// converter, so <c>int</c>, <c>double</c>, <c>bool</c>, <c>DateTime</c> and enums work without extra code.
/// </summary>
public sealed class TextPrompt<T>(string question)
{
    private readonly List<Func<T, string?>> _rules = [];

    /// <summary>The question shown above the box.</summary>
    public string Question { get; } = question;

    /// <summary>The text the box starts with, if any.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>When true the text is shown as stars, as for a password.</summary>
    public bool IsSecret { get; set; }

    /// <summary>Whether an empty answer is accepted. A non-string type always needs a value.</summary>
    public bool AllowEmpty { get; set; }

    /// <summary>Adds a rule. It returns the message to show when the value is not acceptable, or null when it is.</summary>
    public TextPrompt<T> Validate(Func<T, string?> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
        return this;
    }

    /// <summary>Converts and checks <paramref name="text"/>. On failure, <paramref name="error"/> says why.</summary>
    public bool TryParse(string text, out T? value, out string? error)
    {
        value = default;
        error = null;

        if (typeof(T) == typeof(string))
        {
            if (text.Length == 0 && !AllowEmpty)
            {
                error = "Enter a value.";
                return false;
            }
            value = (T)(object)text;
        }
        else
        {
            if (text.Trim().Length == 0)
            {
                error = "Enter a value.";
                return false;
            }
            try
            {
                value = (T?)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(text.Trim());
            }
            catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException or InvalidCastException)
            {
                error = $"'{text.Trim()}' is not a valid {typeof(T).Name}.";
                return false;
            }
        }

        foreach (var rule in _rules)
        {
            error = rule(value!);
            if (error is not null)
            {
                return false;
            }
        }
        return true;
    }
}
