using iConsole.Interfaces;

namespace iTerminal.Core.Services.Nfs;

/// <summary>The <c>nfs://</c> drive's back-end: the exports of a host, and the files of an export.</summary>
public interface INfsDriveService : IEntryListingFileSystem
{
}
