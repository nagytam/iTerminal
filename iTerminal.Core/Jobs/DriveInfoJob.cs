using System.Diagnostics;
﻿using iConsole.Data;
using iConsole.Helper;
using iConsole.Jobs;
using iTerminal.Core.Helper;
using iTerminal.Core.Data;
using iTerminal.Core.Model;
using iTerminal.Core.Services.Cloud;
using Microsoft.Extensions.Options;

namespace iTerminal.Core.Jobs;

public class DriveInfoJob(DriveInfoModel driveInfoModel, IOptions<CloudOptions> cloudOptions) : Job
{
    private DriveInfoModel _driveInfoModel { get; set; } = driveInfoModel;
    private IOptions<CloudOptions> _cloudOptions { get; set; } = cloudOptions;

    /// <summary>Without the cloud configuration the cloud rows are still listed, as not set up.</summary>
    public DriveInfoJob(DriveInfoModel driveInfoModel) : this(driveInfoModel, Options.Create(new CloudOptions()))
    {
    }

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
                var cloud = _cloudOptions.Value;
                AddCloudDrive(Container.ONEDRIVE, ContainerType.OneDrive, cloud.OneDrive.ClientId is { Length: > 0 });
                AddCloudDrive(Container.GOOGLE_DRIVE, ContainerType.GoogleDrive,
                    cloud.GoogleDrive.ClientId is { Length: > 0 } && cloud.GoogleDrive.ClientSecret is { Length: > 0 });
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
            }
            catch (Exception exception)
            {
                // Traced: one drive that fails must not leave the disk manager saying "Reading…" for ever.
                Debug.WriteLine($"{nameof(DriveInfoJob)}.{nameof(RunAsync)} failed: {exception}");
            }
            finally
            {
                // The first pass is over, whether or not every drive answered: "not read yet" is for before that.
                _driveInfoModel.IsLoaded = true;
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
    private const int NameWidth = Drive.NameColumnWidth;
    private const int LabelWidth = 16;
    private const int TypeWidth = 10;

    /// <summary>
    /// Lists one cloud row, once per run. A row is found by its URL type, not its name, because the name
    /// carries the "not configured" suffix. Enabled only when its app registration is set; otherwise the
    /// name says why it cannot be opened.
    /// </summary>
    private void AddCloudDrive(string scheme, ContainerType containerType, bool isConfigured)
    {
        if (_driveInfoModel.Drives.Any(d => d.IsCloud && d.Url.ContainerType == containerType))
        {
            return;
        }
        var name = Fit(scheme, NameWidth);
        if (!isConfigured)
        {
            name += " " + iConsole.Localization.UiText.Get("Drive.CloudNotConfigured", "Cloud sign-in not configured");
        }
        _driveInfoModel.Drives.Add(new Drive
        {
            DriveInfo = null,
            Name = name,
            Url = scheme,
            IsCloudConfigured = isConfigured
        });
    }

    private static string Fit(string? text, int width)
    {
        text ??= string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..(width - 1)] + "…";
    }

    private void Update(Drive drive, CancellationToken cancellationToken)
    {
        try
        {
            // Read here, on the drive's own task: the list draws the bar from it and must not touch the volume.
            drive.UsedFraction = Drive.UsedFractionOf(drive.DriveInfo);
            if (drive.IsEnabled)
            {
                drive.Name += " " + iConsole.Localization.UiText.Get("DriveList.Label", "Label:") + " " + Fit(drive.DriveInfo?.VolumeLabel, LabelWidth);
                drive.Name += " " + Fit(DriveTypeText.Of(drive.DriveInfo?.DriveType), TypeWidth);
            }
            else
            {
                drive.Name += " " + iConsole.Localization.UiText.Get("DriveList.Label", "Label:") + " " + Fit(string.Empty, LabelWidth);
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
            drive.Name += " " + iConsole.Localization.UiText.Get("DriveList.Label", "Label:") + " " + Fit(string.Empty, LabelWidth);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            drive.Name += " " + Fit(DriveTypeText.Of(drive.DriveInfo?.DriveType), TypeWidth);
        }

        var oldDrive = _driveInfoModel.Drives.AsSnapshot().FirstOrDefault(d => 
                            drive?.DriveInfo is not null 
                            && d?.DriveInfo is not null
                            && d.DriveInfo.Name == drive.DriveInfo.Name);
        if (oldDrive is not null)
        {
            var index = _driveInfoModel.Drives.IndexOf(oldDrive);
            // The bar moves with the free space even where the rounded text does not, so a change in either replaces the row.
            if (oldDrive.Name != drive.Name || oldDrive.UsedFraction != drive.UsedFraction)
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
