using iConsole.Data;
using iConsole.Drivers;
using System.Reflection;

namespace iConsole.Test.Drivers;

internal static class ConsoleWindowTestHelper
{
    public static bool TryGetVisibleWindowSize(out Size size)
    {
        if (OperatingSystem.IsWindows())
        {
            var method = typeof(ConsoleWindowSizeHelper).GetMethod("TryGetVisibleWindowInfo", BindingFlags.NonPublic | BindingFlags.Static);
            if (method is not null)
            {
                object?[] parameters = [default(Size), default(Position)];
                if ((bool)method.Invoke(null, parameters)!)
                {
                    size = (Size)parameters[0]!;
                    return true;
                }
            }

            size = default;
            return false;
        }

        try
        {
            size = ConsoleWindowSizeHelper.GetVisibleWindowSize();
            return true;
        }
        catch (IOException)
        {
            size = default;
            return false;
        }
    }
}
