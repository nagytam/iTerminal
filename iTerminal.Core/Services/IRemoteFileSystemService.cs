using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

/// <summary>
/// Common surface of remote drive back-ends (SSH, SMB): connection state plus directory listing on
/// top of the regular <see cref="IFileSystemService"/> operations. Lets
/// <see cref="Model.ContentModels.ContentModelRemoteDrive"/> host any remote drive type; only the
/// protocol-specific connect call stays in the concrete service interfaces.
/// </summary>
public interface IRemoteFileSystemService : IFileSystemService
{
    bool IsConnected { get; }

    IEnumerable<IEntry> List(Url url);
}
