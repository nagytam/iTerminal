namespace iTerminal.Core.Model.Git;

/// <summary>
/// The signature status git reports for a commit with <c>%G?</c>, one letter. A good signature (G) says only that the
/// signature checks against a key in the user's keyring; it does not say the key is trusted or belongs to the person it
/// names, which is the user's decision. Git runs its verifier for each signed commit, so reading the status costs time
/// on a long log.
/// </summary>
public static class GitSignature
{
    /// <summary>The letter for a commit that carries no signature.</summary>
    public const char None = 'N';

    /// <summary>True for any letter other than none; a commit without a signature shows nothing.</summary>
    public static bool IsSigned(char letter) => letter is not (None or '\0' or ' ');

    /// <summary>
    /// A short phrase for the letter, preceded by a separator, or an empty string for an unsigned commit. A known
    /// <paramref name="signer"/> is named at the end of the phrase.
    /// </summary>
    public static string Describe(char letter, string signer = "")
    {
        string phrase = DescribeLetter(letter);
        return phrase.Length > 0 && signer.Length > 0 ? phrase[..^1] + $"; signer: {signer})" : phrase;
    }

    private static string DescribeLetter(char letter) => letter switch
    {
        'G' => " (signed: good signature; the key is not checked for trust)",
        'U' => " (signed: good signature, key validity unknown)",
        'X' => " (signed: good signature, but it has expired)",
        'Y' => " (signed: good signature made by an expired key)",
        'R' => " (signed: good signature made by a revoked key)",
        'B' => " (signed: BAD signature)",
        'E' => " (signed: the signature could not be checked)",
        _ => string.Empty,
    };

    /// <summary>The marker the commit list puts before the subject: empty when unsigned, such as <c>[G] </c> when signed.</summary>
    public static string Marker(char letter) => IsSigned(letter) ? $"[{letter}] " : string.Empty;
}
