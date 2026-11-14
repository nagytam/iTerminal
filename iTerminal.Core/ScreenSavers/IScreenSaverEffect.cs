namespace iTerminal.Core.ScreenSavers;

/// <summary>
/// One screen saver. It keeps its own state between frames and paints a fresh frame into the canvas on each
/// <see cref="Step"/>. It must cope with the canvas changing size: a canvas of a new size means the state is rebuilt for it.
/// </summary>
public interface IScreenSaverEffect
{
    /// <summary>Advances one frame and paints it into <paramref name="canvas"/>, which the caller has cleared.</summary>
    void Step(ScreenSaverCanvas canvas, DateTime now);
}
