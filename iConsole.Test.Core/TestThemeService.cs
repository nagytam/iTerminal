using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Test.Core;

/// <summary>
/// An <see cref="IThemeService"/> serving the stock <see cref="ColorScheme"/>, whose colours are
/// all distinct where a visual state is concerned — so a rendering test can tell focused from
/// normal from disabled by colour alone, and name the colour it expects.
/// </summary>
public sealed class TestThemeService : IThemeService
{
    public TestThemeService() : this(new ColorScheme())
    {
    }

    public TestThemeService(ColorScheme colorScheme)
    {
        CurrentColorScheme = colorScheme;
        ColorSchemes = new Dictionary<Theme, ColorScheme> { { Theme.Default, colorScheme } };
    }

    public ColorScheme CurrentColorScheme { get; }
    public Dictionary<Theme, ColorScheme> ColorSchemes { get; }
}
