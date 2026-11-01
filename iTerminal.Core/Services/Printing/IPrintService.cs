namespace iTerminal.Core.Services.Printing;

/// <summary>
/// Printing a file and managing the queue of a printer. Every member is async by contract — each one
/// is a tool process (PowerShell, CUPS) that can take seconds — and none runs from a key handler.
/// A failure of the tool itself (not installed, no spooler, no answer) comes back as a sentence in
/// the result, never as an exception; only the caller's own cancellation is thrown.
/// </summary>
public interface IPrintService
{
    /// <summary>The printers, the system default flagged.</summary>
    Task<PrinterListing> ListPrintersAsync(CancellationToken cancellationToken = default);

    /// <summary>The jobs waiting in, or being printed by, <paramref name="printer"/>.</summary>
    Task<QueueListing> GetQueueAsync(string printer, CancellationToken cancellationToken = default);

    /// <summary>Sends the local file <paramref name="filePath"/> to <paramref name="printer"/>, <paramref name="copies"/> times.</summary>
    Task<PrintResult> PrintFileAsync(string printer, string filePath, int copies, CancellationToken cancellationToken = default);

    /// <summary>Holds a job so that it is not printed until it is resumed.</summary>
    Task<PrintResult> HoldJobAsync(string printer, int jobId, CancellationToken cancellationToken = default);

    /// <summary>Lets a held job go on.</summary>
    Task<PrintResult> ResumeJobAsync(string printer, int jobId, CancellationToken cancellationToken = default);

    /// <summary>Removes a job from the queue.</summary>
    Task<PrintResult> CancelJobAsync(string printer, int jobId, CancellationToken cancellationToken = default);
}
