namespace iConsole.Certificates;

/// <summary>The extensions F3 opens in the certificate viewer when the file really holds a certificate.</summary>
public static class CertificateFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pem", ".crt", ".cer", ".der" };

    public static bool IsCertificateFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}
