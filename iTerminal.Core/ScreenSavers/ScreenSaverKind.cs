namespace iTerminal.Core.ScreenSavers;

/// <summary>
/// The screen saver chosen in Settings. Stored by name under <see cref="iTerminal.Core.Data.Settings.ScreenSaverEffect"/>;
/// <see cref="Starfield"/> is the default; <see cref="Off"/> turns the screen saver off.
/// </summary>
public enum ScreenSaverKind
{
    Off,
    Starfield,
    MatrixRain,
    Clock,
}
