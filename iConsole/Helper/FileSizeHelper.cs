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

    /// <summary>A size written as a number of bytes, shown as a file size. Text that is not a whole number is shown as it is,
    /// rather than throwing from the listing that asked.</summary>
    public static string ToFileSize(this string value) =>
        long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var bytes)
            ? bytes.ToFileSize()
            : value;

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
