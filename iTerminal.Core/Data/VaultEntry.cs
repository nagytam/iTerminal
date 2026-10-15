namespace iTerminal.Core.Data;

public class VaultEntry
{
    public string ProfileId { get; set; } = string.Empty;

    public string EncryptedSecret { get; set; } = string.Empty;
}
