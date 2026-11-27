using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// A page of the Development studio's settings dialog. <see cref="Load"/> fills the controls from the settings without saving
/// anything; a change the user makes saves the page through <see cref="Save"/>.
/// </summary>
public abstract class StudioSettingsTabPage : TabPage
{
    /// <summary>True while the controls are being filled from the settings, so that filling them is not read as the user changing them.</summary>
    private bool _loading;

    protected StudioSettingsTabPage(string name, ISettingsService settings, IThemeService themeService)
        : base(name, themeService)
    {
        Settings = settings;
    }

    protected ISettingsService Settings { get; }

    public override Task OnInitializeAsync()
    {
        Load();
        return base.OnInitializeAsync();
    }

    /// <summary>Fills the page from the settings; nothing is saved by it.</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            OnLoad();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Fills the controls from the settings. Called with saving turned off.</summary>
    protected abstract void OnLoad();

    /// <summary>Saves what the page shows.</summary>
    protected abstract void Save();

    /// <summary>A control of the page changed: saved, unless the page is being filled from the settings.</summary>
    protected void OnChanged(object? sender, EventArgs e)
    {
        if (!_loading)
        {
            Save();
        }
    }

    /// <summary>A stored setting as its enum value, or the fallback when it is missing or names no value of the enum.</summary>
    protected static T Parse<T>(string? text, T fallback) where T : struct, Enum =>
        Enum.TryParse(text, out T value) && Enum.IsDefined(value) ? value : fallback;

    /// <summary>Places a control at a column and row of the page; a row below the page's height gets no height and paints nothing.</summary>
    protected void Place(Control control, long x, int y, long width)
    {
        control.Position = new Position(x, y);
        control.ViewSize = new Size(width, y < (int)ViewSize.Height ? 1 : 0);
    }
}
