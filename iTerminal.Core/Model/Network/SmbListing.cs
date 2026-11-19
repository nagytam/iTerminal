namespace iTerminal.Core.Model.Network;

/// <summary>
/// The shares a host lists for one login, and the SMB protocol version the session negotiated: 2 or 3 (0 when it is
/// not known). The version is what the folder selector shows as a hint, so a user can tell which dialect a host speaks.
/// </summary>
public sealed record SmbListing(IReadOnlyList<string> Shares, int SmbVersion = 0);
