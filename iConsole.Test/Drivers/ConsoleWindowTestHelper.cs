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

            size = null!;
            return false;
        }

        // Without a console there is no visible window to compare against. The properties read as 0 or
        // throw, GetVisibleWindowSize then answers with its 80x24 default, and Normalize (which reads the
        // same properties) leaves the size alone: the two fallbacks disagree, so the tests must take the
        // no-console branch.
        try
        {
            if (Console.WindowWidth <= 0 || Console.WindowHeight <= 0)
            {
                size = null!;
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentOutOfRangeException)
        {
            size = null!;
            return false;
        }

        size = ConsoleWindowSizeHelper.GetVisibleWindowSize();
        return true;
    }
}
