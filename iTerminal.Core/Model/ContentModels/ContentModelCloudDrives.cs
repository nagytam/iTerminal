using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Cloud;
using Serilog;
#pragma warning disable S1075 // iTerminal's own URLs (not file-system paths) use '/' on every platform.

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

/// <summary>A panel on Dropbox (<c>dropbox://[account]/path</c>), signed in with the app's refresh token from the password manager.</summary>
public class ContentModelDropboxDrive(
    string name,
    ISettingsService? settingsService,
    IDropboxFileSystemService dropboxFileSystemService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.DROPBOX;

    public override ContainerType ContainerType => ContainerType.Dropbox;

    public override IFileSystem FileSystem => dropboxFileSystemService;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken) =>
        dropboxFileSystemService.ListAsync(url, cancellationToken);

    protected override string DescribeLocation() => $"dropbox: {(Url?.Path ?? "/")}";
}

/// <summary>
/// A storage account, as a row of <c>azblob://</c>: Enter opens it, and it is not a file to copy or
/// delete. The rows come from the password manager's entries, so the root works with no network.
/// </summary>
public sealed record AzureAccountEntry : VirtualDriveEntry, IDriveObjectEntry
{
    public AzureAccountEntry(Url url, string name)
        : base(url, name, "Account")
    {
        Detail = "Account";
    }
}

/// <summary>
/// A panel on Azure Blob Storage (<c>azblob://account/container/path</c>). The root lists the storage
/// accounts the password manager holds, an account lists its containers, and below a container the
/// blobs are listed as folders and files. The credentials are looked up per request by the service, so
/// the account in the URL is the one that signs it.
/// </summary>
public class ContentModelAzureBlobDrive(
    string name,
    ISettingsService? settingsService,
    IAzureBlobFileSystemService azureBlobFileSystemService,
    IPasswordManager? passwordManager,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.AZURE_BLOB;

    public override ContainerType ContainerType => ContainerType.AzureBlob;

    public override IFileSystem FileSystem => azureBlobFileSystemService;

    /// <summary>The account list is the root; an account's own root leads back up to it, not out of the drive.</summary>
    protected override bool IsDriveRoot(Url url) => url.Host.Length == 0;

    /// <summary>The account list cannot be written to: an account is added in the password manager, not here.</summary>
    public override bool IsReadOnly => Url is not null && Url.Host.Length == 0;

    public override bool CanMark => Url is null || (Url.Host.Length != 0 && Url.Path.Trim('/').Length != 0);

    protected override IEntry ParentEntry(Url url)
    {
        if (!IsDriveRoot(url) && url.Path.Trim('/').Length == 0)
        {
            return new VirtualDriveEntry(new Url(Container.AZURE_BLOB), "..", "Up");
        }

        return base.ParentEntry(url);
    }

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        if (url.Host.Length == 0)
        {
            IReadOnlyList<IEntry> accounts = [.. AzureBlobCredentialResolver.SavedAccounts(passwordManager)
                .Select(account => (IEntry)new AzureAccountEntry(new Url(Container.AZURE_BLOB + account + "/"), account))];
            return Task.FromResult(accounts);
        }

        return azureBlobFileSystemService.ListAsync(url, cancellationToken);
    }

    protected override string DescribeLocation()
    {
        var url = Url;
        return url is null || url.Host.Length == 0 ? "azblob: accounts" : $"azblob://{url.Host}{url.Path}";
    }
}
