using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Cloud;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A panel on an S3 bucket (<c>s3://[accessKeyId@]bucket/key</c>); without a bucket it lists the
/// account's buckets. Sign-in is resolved on the listing's background task
/// (<see cref="S3CredentialResolver"/>), where a password manager lookup may take its time.
/// </summary>
public class ContentModelS3Drive(
    string name,
    ISettingsService? settingsService,
    IS3FileSystemService s3FileSystemService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null,
    IPasswordManager? passwordManager = null,
    S3Options? options = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.S3;

    public override ContainerType ContainerType => ContainerType.S3;

    public override IFileSystem FileSystem => s3FileSystemService;

    /// <summary>The bucket list is the root; a bucket's own root leads back up to it, not out of the drive.</summary>
    protected override bool IsDriveRoot(Url url) => url.Host.Length == 0;

    /// <summary>Buckets are listed, but nothing can be created or copied into the list of them.</summary>
    public override bool IsReadOnly => Url is not null && Url.Host.Length == 0;

    public override bool CanMark => Url is null || Url.Host.Length != 0;

    protected override IEntry ParentEntry(Url url)
    {
        if (!IsDriveRoot(url) && url.Path.Trim('/').Length == 0)
        {
            var user = string.IsNullOrEmpty(url.Username) ? string.Empty : url.Username + "@";
            return new VirtualDriveEntry(new Url(Container.S3 + user + "/"), "..", "Up");
        }

        return base.ParentEntry(url);
    }

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var credentials = S3CredentialResolver.Resolve(url.Username, passwordManager, options);
        s3FileSystemService.Connect(credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.Region, credentials.Endpoint);
        return s3FileSystemService.ListAsync(url, cancellationToken);
    }

    protected override string DescribeLocation()
    {
        var url = Url;
        return url is null || url.Host.Length == 0 ? "s3: buckets" : $"s3://{url.Host}{url.Path}";
    }
}

/// <summary>A panel on OneDrive (<c>onedrive://[account]/path</c>), signed in through the device flow.</summary>
public class ContentModelOneDriveDrive(
    string name,
    ISettingsService? settingsService,
    IOneDriveFileSystemService oneDriveFileSystemService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.ONEDRIVE;

    public override ContainerType ContainerType => ContainerType.OneDrive;

    public override IFileSystem FileSystem => oneDriveFileSystemService;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        oneDriveFileSystemService.Connect(url.Host);
        return oneDriveFileSystemService.ListAsync(url, cancellationToken);
    }

    protected override string DescribeLocation() => $"onedrive: {(Url?.Path ?? "/")}";
}

/// <summary>A panel on Google Drive (<c>gdrive://[account]/path</c>), signed in through the device flow.</summary>
public class ContentModelGoogleDriveDrive(
    string name,
    ISettingsService? settingsService,
    IGoogleDriveFileSystemService googleDriveFileSystemService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.GOOGLE_DRIVE;

    public override ContainerType ContainerType => ContainerType.GoogleDrive;

    public override IFileSystem FileSystem => googleDriveFileSystemService;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        googleDriveFileSystemService.Connect(url.Host);
        return googleDriveFileSystemService.ListAsync(url, cancellationToken);
    }

    protected override string DescribeLocation() => $"gdrive: {(Url?.Path ?? "/")}";
}
