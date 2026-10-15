using iConsole.Data;

namespace iTerminal.Core.Services;

/// <summary>
/// AFC (Apple File Conduit) back-end for an <c>iphone://</c> drive. Extends the common
/// <see cref="IRemoteFileSystemService"/> surface with a <see cref="Connect"/> call that opens an
/// AFC session to a device by its UDID. See <see cref="Model.ContentModels.ContentModelIPhoneDrive"/>.
/// </summary>
public interface IIPhoneFileSystemService : IRemoteFileSystemService
{
    /// <summary>Opens (or reuses) an AFC session to the device identified by <paramref name="udid"/>.</summary>
    void Connect(string udid);
}
