using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// Single-line text input that renders every character as '●'.
/// Inherits all editing behaviour <i>and</i> the whole render path from <see cref="TextBox"/>
/// (label, well, scrolling, cursor, selection); only <see cref="DisplayText"/> is masked, so the
/// actual password is never written to the buffer. Autocompletion is never drawn: a suggestion
/// shown in clear next to a masked value would give the value away.
/// </summary>
public class PasswordBox : TextBox
{
    private const char MaskChar = '●';

    public PasswordBox(
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(beepService, dateTimeProviderService, themeService)
    {
    }

    protected override string DisplayText => new(MaskChar, Text.Length);

    protected override bool ShowsCompletion => false;
}
