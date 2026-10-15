using iConsole.Drivers;

namespace iConsole.Services;

public class BeepService(IConsoleDriver consoleDriver) : IBeepService
{
    private readonly Drivers.IConsoleDriver _consoleDriver = consoleDriver ?? NullConsoleDriver.Instance;

    public void Beep()
    {
        _consoleDriver.Beep(); 
    }
}
