namespace iTerminal.Core.Model.Git;

/// <summary>One commit of the log. <paramref name="Signature"/> is the letter of git's <c>%G?</c> (see <see cref="GitSignature"/>).</summary>
public sealed record GitCommit(string ShortHash, string Subject, string Author, string RelativeDate, char Signature = GitSignature.None);
