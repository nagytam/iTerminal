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

        // Without a console there is no visible window to compare against. GetVisibleWindowSize would
        // answer with its 80x24 default, and Normalize (which reads the same properties) would leave
        // the size alone: the two fallbacks disagree, so the tests must take the no-console branch.
        try
        {
            _ = Console.WindowWidth;
        }
        catch (Exception exception) when (exception is IOException or ArgumentOutOfRangeException)
        {
            size = default;
            return false;
        }

        size = ConsoleWindowSizeHelper.GetVisibleWindowSize();
        return true;
    }
}
