namespace iTerminal.Core.Model.Git;

/// <summary>A single entry from <c>git log</c>.</summary>
public sealed record GitCommit(string ShortHash, string Subject, string Author, string RelativeDate);
