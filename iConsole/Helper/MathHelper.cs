namespace iConsole.Helper;

public static class MathHelper
{
    public static int Min(int i1, int i2, int i3)
    {
        return Math.Min(i1, Math.Min(i2, i3));
    }

    public static int Range(this int i, int min, int max)
    {
        if (i < min)
        {
            i = min;
        }
        else if (i > max)
        {
            i = max;
        }
        return i;
    }

    public static long Range(this long l, long min, long max)
    {
        if (l < min)
        {
            l = min;
        }
        else if (l > max)
        {
            l = max;
        }
        return l;
    }
}
