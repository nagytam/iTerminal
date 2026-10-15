namespace iConsole.Helper;

public static class SystemHelper
{
    public static long GetAvailableMemoryInMBs()
    {
        var gcMemoryInfo = GC.GetGCMemoryInfo();
        long installedMemory = gcMemoryInfo.TotalAvailableMemoryBytes;
        var physicalMemory = installedMemory / 1048576;
        return physicalMemory;
    }
}
