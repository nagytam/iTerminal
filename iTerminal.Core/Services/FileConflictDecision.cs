using iConsole.Data;

namespace iTerminal.Core.Services;

/// <summary>
/// What a file transfer does about one conflict: skip it (the source is kept), or write to
/// <see cref="Target"/>. <see cref="Replace"/> says the write overwrites an existing file.
/// </summary>
public sealed record FileConflictDecision(bool Skip, Url Target, bool Replace);
