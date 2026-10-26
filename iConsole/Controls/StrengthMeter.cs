using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>How hard a password is to guess, in four steps (and <see cref="None"/> for an empty one).</summary>
public enum PasswordStrength
{
    None,
    Weak,
    Fair,
    Good,
    Strong,
}

/// <summary>
/// A one-row password-strength indicator (<c>▰▰▰▱ Good</c>) that updates as <see cref="Password"/> changes. The
/// rating is a rule of thumb — length, the mix of letters, digits and symbols, and a veto for the obvious
/// (repeats, runs, the passwords everyone tries first) — meant to nudge, not to certify. The control keeps only
/// the rating, never the text.
/// </summary>
public class StrengthMeter : Control
{
    private static readonly string[] Common =
    [
        "password", "passw0rd", "123456", "12345678", "qwerty", "letmein", "welcome", "admin", "iloveyou", "abc123", "111111",
    ];

    private PasswordStrength _strength;

    public StrengthMeter(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Size(12, 1);
    }

    public PasswordStrength Strength => _strength;

    /// <summary>The password to rate; set it again whenever it changes. Only the resulting <see cref="Strength"/> is kept.</summary>
    public string Password
    {
        set
        {
            PasswordStrength rated = Rate(value);
            if (rated != _strength)
            {
                _strength = rated;
                StateHasChanged();
            }
        }
    }

    /// <summary>Rates <paramref name="password"/>.</summary>
    public static PasswordStrength Rate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return PasswordStrength.None;
        }

        if (password.Length < 6 || IsObvious(password))
        {
            return PasswordStrength.Weak;
        }

        int score = 0;
        if (password.Length >= 8)
        {
            score++;
        }

        if (password.Length >= 12)
        {
            score++;
        }

        if (password.Length >= 16)
        {
            score++;
        }

        int classes = (password.Any(char.IsLower) ? 1 : 0)
            + (password.Any(char.IsUpper) ? 1 : 0)
            + (password.Any(char.IsDigit) ? 1 : 0)
            + (password.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        if (classes >= 3)
        {
            score++;
        }

        if (classes == 4 && password.Length >= 10)
        {
            score++;
        }

        return score switch
        {
            <= 1 => PasswordStrength.Weak,
            2 => PasswordStrength.Fair,
            3 => PasswordStrength.Good,
            _ => PasswordStrength.Strong,
        };
    }

    /// <summary>One repeated character, a run of consecutive characters ("abcdef", "98765"), or one of the usual suspects.</summary>
    private static bool IsObvious(string password)
    {
        if (password.Distinct().Count() == 1)
        {
            return true;
        }

        string lower = password.ToLowerInvariant();
        if (Common.Any(c => lower == c || (lower.StartsWith(c, StringComparison.Ordinal) && lower.Length <= c.Length + 2)))
        {
            return true;
        }

        bool ascending = true;
        bool descending = true;
        for (int i = 1; i < lower.Length; i++)
        {
            ascending &= lower[i] == lower[i - 1] + 1;
            descending &= lower[i] == lower[i - 1] - 1;
        }

        return ascending || descending;
    }

    public static string NameOf(PasswordStrength strength) => strength switch
    {
        PasswordStrength.Weak => "Weak",
        PasswordStrength.Fair => "Fair",
        PasswordStrength.Good => "Good",
        PasswordStrength.Strong => "Strong",
        _ => string.Empty,
    };

    public static Color ColorOf(PasswordStrength strength, Color neutral) => strength switch
    {
        PasswordStrength.Weak => Color.Red,
        PasswordStrength.Fair => Color.Yellow,
        PasswordStrength.Good => Color.Green,
        PasswordStrength.Strong => Color.Cyan,
        _ => neutral,
    };

    /// <summary>The bar and the name, e.g. <c>▰▰▰▱ Good</c>; just the empty bar for no password.</summary>
    public static string Render(PasswordStrength strength)
    {
        int filled = Math.Clamp((int)strength, 0, 4);
        string bar = new string('▰', filled) + new string('▱', 4 - filled);
        string name = NameOf(strength);
        return name.Length == 0 ? bar : bar + " " + name;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        string text = Render(_strength);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize,
            new ConxelString((text.Length > width ? text[..width] : text.PadRight(width)),
                ColorOf(_strength, colorScheme.DisabledForeground), colorScheme.Background));
    }
}
