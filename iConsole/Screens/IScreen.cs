using iConsole.Controls;
using iConsole.Data;
using iConsole.Desktops;

namespace iConsole.Screens;

public interface IScreen : IControl
{
    IDesktop Desktop { get; set; }
    void OnWindowSizeChanged(object? sender, Size e);
}
