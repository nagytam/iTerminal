namespace iTerminal.Core.Model.Git;

/// <summary>
/// One commit of the log. <paramref name="Signature"/> is the letter of git's <c>%G?</c> (see <see cref="GitSignature"/>);
/// <paramref name="Signer"/> is the name git's <c>%GS</c> gives for a signed commit, and empty when it is unsigned or unknown.
/// </summary>
public sealed record GitCommit(string ShortHash, string Subject, string Author, string RelativeDate, char Signature = GitSignature.None, string Signer = "");
