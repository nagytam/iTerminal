namespace iTerminal.Core.Services.Drives;

/// <summary>What an action did, in a sentence a footer or a toast can show.</summary>
public sealed record DriveActionResult(bool Success, string Message);

/// <summary>What BitLocker says about a volume.</summary>
public enum BitLockerState
{
    /// <summary>This platform or this kind of drive has no BitLocker (Linux, a network share, a RAM disk).</summary>
    NotApplicable,

    /// <summary>The status could not be read.</summary>
    Unavailable,

    /// <summary>Not encrypted, or protection is off.</summary>
    Off,

    /// <summary>Protected by BitLocker.</summary>
    On,

    /// <summary>A state the app has no word for; the raw value is in the text.</summary>
    Unknown,
}

public sealed record BitLockerStatus(BitLockerState State, string Text);

/// <summary>How to format a volume.</summary>
/// <param name="FileSystem">One of <see cref="DriveActionsService.FileSystems"/>.</param>
/// <param name="Label">The new volume label; may be empty.</param>
/// <param name="Quick">Skip the full surface pass; the usual choice.</param>
public sealed record DriveFormatOptions(string FileSystem, string Label, bool Quick = true);

/// <summary>
/// The things done to a drive rather than to its files: take it away safely, format it, mount a disk
/// image as one, and say whether BitLocker protects it. Every member is async by contract — each
/// one is a tool process that can take seconds (a format, a flush before an eject) — and none runs
/// from a key handler.
/// </summary>
public interface IDriveActionsService
{
    /// <summary>False where none of the tooling is available (macOS): the actions say so rather than fail one by one.</summary>
    bool IsSupported { get; }

    /// <summary>Whether <see cref="FormatAsync"/> exists on this platform.</summary>
    bool CanFormat { get; }

    /// <summary>Flushes, dismounts and powers off the drive at <paramref name="root"/> (<c>E:\</c>, <c>/media/x</c>).</summary>
    Task<DriveActionResult> EjectAsync(string root, CancellationToken cancellationToken = default);

    /// <summary>Formats the volume at <paramref name="root"/>. Everything on it is lost.</summary>
    Task<DriveActionResult> FormatAsync(string root, DriveFormatOptions options, CancellationToken cancellationToken = default);

    /// <summary>Attaches an ISO, VHD or VHDX as a drive; the message says which.</summary>
    Task<DriveActionResult> MountImageAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>Detaches an image attached earlier.</summary>
    Task<DriveActionResult> UnmountImageAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>The BitLocker state of the volume at <paramref name="root"/>.</summary>
    Task<BitLockerStatus> GetBitLockerStatusAsync(string root, CancellationToken cancellationToken = default);

    /// <summary>
    /// Why <paramref name="root"/> must not be formatted — the system drive, one a panel or the app is
    /// using, a network or optical drive — or null when it may. <paramref name="foldersInUse"/> are
    /// folders something holds open.
    /// </summary>
    string? WhyCannotFormat(string root, IEnumerable<string> foldersInUse);
}
