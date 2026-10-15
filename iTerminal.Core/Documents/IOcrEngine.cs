namespace iTerminal.Core.Documents;

/// <summary>
/// Turns the pixels of an image into text. An interface with exactly one production implementation
/// because the alternative — <see cref="Extractors.ImageTextExtractor"/> shelling out directly — is
/// untestable: every test of image indexing would then depend on what happens to be installed on
/// the machine running it.
/// </summary>
public interface IOcrEngine
{
    /// <summary>A name for the status line, e.g. "Tesseract 5.3.4".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether the engine can actually run here. Probed once and cached: an indexing pass asks this
    /// per file, and the answer cannot change while the app is running.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Reads <paramref name="imagePath"/> and returns whatever text it found, or an empty string.
    /// Never throws — an OCR engine that fails on one image must not fail the scan.
    /// </summary>
    string Recognize(string imagePath, CancellationToken cancellationToken = default);
}
