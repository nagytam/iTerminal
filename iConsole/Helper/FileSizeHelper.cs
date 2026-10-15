namespace iConsole.Helper;

public static class FileSizeHelper
{
    public static string ToFileSize(this long value)
    {
        string[] unitPrefixes = [" B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];
        for (int i = 0; i < unitPrefixes.Length; i++)
        {
            if (value < Math.Pow(1024, i + 1))
            {
                return ThreeNonZeroDigits(value / Math.Pow(1024, i)) + " " + unitPrefixes[i];
            }
        }

        return ThreeNonZeroDigits(value / Math.Pow(1024, unitPrefixes.Length - 1)) + " " + unitPrefixes[^1];
    }

    public static string ToFileSize(this string value)
    {
        var longValue = long.Parse(value);
        return longValue.ToFileSize();
    }

    private static string ThreeNonZeroDigits(double value)
    {
        if (value >= 100)
        {
            return value.ToString("0");
        }
        else if (value >= 10)
        {
            return value.ToString("0");
        }
        else if (value < 10)
        {
            return value.ToString("0");
        }
        else
        {
            return value.ToString("0.00");
        }
    }
}
