namespace iTerminal.Core.Services.Streams;

/// <summary>One alternate data stream (NTFS) or extended attribute (Linux, macOS) of a file or folder.</summary>
/// <param name="Name">The stream's name without the leading colon and the <c>:$DATA</c> suffix, or the attribute's full name (<c>user.comment</c>).</param>
/// <param name="Size">The length in bytes.</param>
public sealed record StreamItem(string Name, long Size);

/// <summary>What <see cref="IStreamAttributeService.ListAsync"/> found; <see cref="Failure"/> is a sentence when the list could not be read.</summary>
public sealed record StreamListing(IReadOnlyList<StreamItem> Items, string? Failure = null);

/// <summary>The first bytes of a stream or attribute; <see cref="TotalSize"/> is its full length.</summary>
public sealed record StreamData(byte[] Bytes, long TotalSize, string? Failure = null)
{
    public bool IsTruncated => TotalSize > Bytes.Length;
}

/// <summary>How a delete went, as a sentence a dialog can show.</summary>
public sealed record StreamResult(bool Success, string Message);
