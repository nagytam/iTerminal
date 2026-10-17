using iConsole.Data;
using iConsole.Helper;
using iConsole.Jobs;
using iTerminal.Core.Data;
using iTerminal.Core.Model;

namespace iTerminal.Core.Jobs;

public class DriveInfoJob(DriveInfoModel driveInfoModel) : Job
{
    private DriveInfoModel _driveInfoModel { get; set; } = driveInfoModel;

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var tasks = new List<Task>();
                #region
                var memDrive = new Drive
                {
                    DriveInfo = null,
                    Name = Fit(Container.MEMORY_DRIVE, NameWidth),
                    Url = Container.MEMORY_DRIVE
                };
                if (!_driveInfoModel.Drives.Any(d => d.Name.StartsWith(Container.MEMORY_DRIVE)))
                {
                    _driveInfoModel.Drives.Add(memDrive);
                }
                #endregion
                var driveInfos = DriveInfo.GetDrives().ToList();
                if (OperatingSystem.IsLinux())
                {
                    driveInfos = driveInfos
                        .Where(d => d.DriveType != DriveType.Ram)
                        .Where(d => !d.Name.StartsWith("/boot/"))
                        .Where(d => !d.Name.StartsWith("/proc/"))
                        .Where(d => !d.Name.StartsWith("/run/"))
                        .Where(d => !d.Name.StartsWith("/snap/"))
                        .Where(d => !d.Name.StartsWith("/sys/"))
                        .Where(d => !d.Name.StartsWith("/var/"))
                        .ToList();
                }
                foreach (var driveInfo in driveInfos)
                {
                    // IsReady is asked on the drive's own task, not here: it can block for seconds
                    // on a card reader or a sleeping disk, and this thread waits on the tasks
                    // cancellably — asked here, one slow drive held up every shutdown (F10 Quit
                    // waited out the host's five-second timeout).
                    tasks.Add(Task.Run(() =>
                    {
                        if (cancellationToken.IsCancellationRequested || !driveInfo.IsReady)
                        {
                            return;
                        }
                        var drive = new Drive
                        {
                            DriveInfo = driveInfo,
                            Name = Fit(driveInfo.Name, NameWidth),
                            Url = new Url(driveInfo.Name)
                        };
                        Update(drive, cancellationToken);
                    }, cancellationToken));
                }

                Task.WaitAll([.. tasks], cancellationToken);
                _driveInfoModel.IsLoaded = true;
            }
            catch
            {
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                // WaitHandle.WaitOne returns immediately once cancellationToken fires, unlike
                // Thread.Sleep, so shutdown (e.g. F10 Quit) isn't stalled waiting out the interval.
                cancellationToken.WaitHandle.WaitOne(5000);
            }
        }
        return Task.CompletedTask;
    }

    // The list shows the drive as one string, so each column has a fixed width: a long mount point or
    // label is cut with an ellipsis instead of pushing the type and the sizes out of line.
    private const int NameWidth = 20;
    private const int LabelWidth = 16;
    private const int TypeWidth = 10;

    private static string Fit(string? text, int width)
    {
        text ??= string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..(width - 1)] + "…";
    }

    private void Update(Drive drive, CancellationToken cancellationToken)
    {
        try
        {
            if (drive.IsEnabled)
            {
                drive.Name += " Label: " + Fit(drive.DriveInfo?.VolumeLabel, LabelWidth);
                drive.Name += " " + Fit(drive.DriveInfo?.DriveType.ToString(), TypeWidth);
            }
            else
            {
                drive.Name += " Label: " + Fit(string.Empty, LabelWidth);
                drive.Name += " " + Fit(string.Empty, TypeWidth);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (drive.IsEnabled)
            {
                drive.Name += " " + drive.DriveInfo?.TotalFreeSpace.ToFileSize().PadLeft(7);
                drive.Name += " / " + drive.DriveInfo?.TotalSize.ToFileSize().PadLeft(7);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        catch
        {
            drive.Name = Fit(drive.DriveInfo?.Name, NameWidth);
            drive.Name += " Label: " + Fit(string.Empty, LabelWidth);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            drive.Name += " " + Fit(drive.DriveInfo?.DriveType.ToString(), TypeWidth);
        }

        var oldDrive = _driveInfoModel.Drives.AsSnapshot().FirstOrDefault(d => 
                            drive?.DriveInfo is not null 
                            && d?.DriveInfo is not null
                            && d.DriveInfo.Name == drive.DriveInfo.Name);
        if (oldDrive is not null)
        {
            var index = _driveInfoModel.Drives.IndexOf(oldDrive);
            if (oldDrive.Name != drive.Name)
            {
                _driveInfoModel.Drives.ReplaceAt(index, drive);
            }
        }
        else
        {
            _driveInfoModel.Drives.Add(drive);
        }
    }
}
