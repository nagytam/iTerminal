namespace iTerminal.Core.Services;

/// <summary>
/// Replaces a file's contents without ever leaving it half written: the new bytes go to a temporary file beside the
/// original, which is then moved over it. A failure before the move leaves the original as it was.
/// </summary>
public static class FileReplacement
{
    public static void Replace(string path, byte[] content)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
                // Left behind only when it cannot be removed; the original is intact either way.
            }
        }
    }
}
