using System.Globalization;
using System.Text.RegularExpressions;

namespace iConsole.Model.Validations;

/// <summary>One rule a value must meet. <see cref="Validate"/> returns null when the value is fine, otherwise the message to show.</summary>
public interface IInputValidator
{
    string? Validate(string text);
}

/// <summary>A validator written as a function.</summary>
public sealed class DelegateValidator(Func<string, string?> check) : IInputValidator
{
    public string? Validate(string text) => check(text);
}

/// <summary>
/// The usual rules, ready to add to a <see cref="ValidatorPipeline"/>. Except for <see cref="Required"/> a rule accepts an
/// empty value, so an optional field can still carry a format; combine with <see cref="Required"/> to demand one.
/// </summary>
public static class InputValidators
{
    public static IInputValidator Required(string message = "A value is required") =>
        new DelegateValidator(text => string.IsNullOrWhiteSpace(text) ? message : null);

    public static IInputValidator MinLength(int length, string? message = null) =>
        new DelegateValidator(text => text.Length > 0 && text.Length < length ? message ?? $"At least {length} characters" : null);

    public static IInputValidator MaxLength(int length, string? message = null) =>
        new DelegateValidator(text => text.Length > length ? message ?? $"At most {length} characters" : null);

    /// <summary>The whole value must match <paramref name="pattern"/>.</summary>
    public static IInputValidator Pattern(string pattern, string message)
    {
        var regex = new Regex("^(?:" + pattern + ")$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return new DelegateValidator(text =>
        {
            if (text.Length == 0)
            {
                return null;
            }

            try
            {
                return regex.IsMatch(text) ? null : message;
            }
            catch (RegexMatchTimeoutException)
            {
                return message;
            }
        });
    }

    /// <summary>A whole number, optionally within <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static IInputValidator Integer(long? min = null, long? max = null, string? message = null) =>
        new DelegateValidator(text =>
        {
            if (text.Length == 0)
            {
                return null;
            }

            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            {
                return message ?? "Enter a whole number";
            }

            return OutOfRange(value, min, max, message);
        });

    /// <summary>A number with an optional fraction, optionally within <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static IInputValidator Number(double? min = null, double? max = null, string? message = null) =>
        new DelegateValidator(text =>
        {
            if (text.Length == 0)
            {
                return null;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            {
                return message ?? "Enter a number";
            }

            return OutOfRange(value, min, max, message);
        });

    private static string? OutOfRange<T>(T value, T? min, T? max, string? message) where T : struct, IComparable<T>
    {
        if (min is { } low && value.CompareTo(low) < 0)
        {
            return message ?? $"At least {low}";
        }

        if (max is { } high && value.CompareTo(high) > 0)
        {
            return message ?? $"At most {high}";
        }

        return null;
    }

    /// <summary>Something that looks like an address: text, an @, a domain with a dot. It does not check that the mailbox exists.</summary>
    public static IInputValidator Email(string message = "Enter an email address") =>
        Pattern(@"[^@\s]+@[^@\s.]+(\.[^@\s.]+)+", message);

    /// <summary>One of <paramref name="allowed"/>, ignoring case.</summary>
    public static IInputValidator OneOf(IEnumerable<string> allowed, string? message = null)
    {
        string[] values = [.. allowed];
        return new DelegateValidator(text =>
            text.Length == 0 || values.Contains(text, StringComparer.CurrentCultureIgnoreCase)
                ? null
                : message ?? "Not one of: " + string.Join(", ", values));
    }

    /// <summary>The value as an <see cref="InputMask"/> needs it: every required slot filled. Give it the raw text.</summary>
    public static IInputValidator MaskComplete(InputMask mask, string message = "Fill in every position") =>
        new DelegateValidator(text => text.Length == 0 || mask.IsComplete(text) ? null : message);
}

/// <summary>
/// Runs a list of <see cref="IInputValidator"/> over a value, in order. It reads the value from <c>textSource</c> each time it is
/// asked and re-checks only when the value changed, so a text box can ask on every draw. With <see cref="StopOnFirstError"/>
/// (the default) the first failing rule gives <see cref="ErrorMessage"/> and the rest are skipped; otherwise
/// <see cref="Errors"/> lists them all.
/// </summary>
public sealed class ValidatorPipeline(Func<string> textSource) : IValidationModel
{
    private readonly Lock _gate = new();
    private readonly List<IInputValidator> _validators = [];
    private string? _checkedText;
    private IReadOnlyList<string> _errors = [];
    private bool _stopOnFirstError = true;

    public IReadOnlyList<IInputValidator> Validators
    {
        get { lock (_gate) { return [.. _validators]; } }
    }

    public bool StopOnFirstError
    {
        get { lock (_gate) { return _stopOnFirstError; } }
        set { lock (_gate) { _stopOnFirstError = value; _checkedText = null; } }
    }

    public ValidatorPipeline Add(IInputValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        lock (_gate)
        {
            _validators.Add(validator);
            _checkedText = null;
        }

        return this;
    }

    public ValidatorPipeline Add(Func<string, string?> check) => Add(new DelegateValidator(check));

    public void Clear()
    {
        lock (_gate)
        {
            _validators.Clear();
            _checkedText = null;
        }
    }

    /// <summary>Every message from the rules that failed (just the first with <see cref="StopOnFirstError"/>).</summary>
    public IReadOnlyList<string> Errors
    {
        get
        {
            Refresh();
            lock (_gate) { return _errors; }
        }
    }

    public bool IsValid => Errors.Count == 0;

    public string ErrorMessage => Errors.Count == 0 ? string.Empty : Errors[0];

    /// <summary>Checks the value now, whether or not it changed.</summary>
    public void Validate(DateTime now)
    {
        lock (_gate)
        {
            _checkedText = null;
        }

        Refresh();
    }

    private void Refresh()
    {
        string text = textSource() ?? string.Empty;
        lock (_gate)
        {
            if (_checkedText == text)
            {
                return;
            }

            var errors = new List<string>();
            foreach (IInputValidator validator in _validators)
            {
                string? error;
                try
                {
                    error = validator.Validate(text);
                }
                catch (Exception exception)
                {
                    // A rule that throws must not take the box down; it counts as failed.
                    error = exception.Message;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    errors.Add(error);
                    if (_stopOnFirstError)
                    {
                        break;
                    }
                }
            }

            _errors = errors;
            _checkedText = text;
        }
    }
}
