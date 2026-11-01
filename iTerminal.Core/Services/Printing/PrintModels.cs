namespace iTerminal.Core.Services.Printing;

/// <summary>A printer the system knows. <see cref="Status"/> is the system's own word for it ("idle", "Normal", "Offline").</summary>
public sealed record Printer(string Name, bool IsDefault, string Status);

public enum PrintJobState
{
    Queued,
    Printing,
    Held,
    Error,
}

/// <summary>
/// One job in a printer's queue. A job is named by the printer and the id together: on Windows the
/// ids count per printer, so an id alone could be another printer's job.
/// </summary>
public sealed record PrintJob(string Printer, int Id, string Document, string Owner, long Size, PrintJobState State);

/// <summary>What an action did, in a sentence a status line or a toast can show.</summary>
public sealed record PrintResult(bool Success, string Message);

/// <summary>The printers, or the sentence that says why they could not be listed (no spooler, no CUPS).</summary>
public sealed record PrinterListing(IReadOnlyList<Printer> Printers, string? Failure = null);

/// <summary>A printer's queue, or the sentence that says why it could not be read.</summary>
public sealed record QueueListing(IReadOnlyList<PrintJob> Jobs, string? Failure = null);
