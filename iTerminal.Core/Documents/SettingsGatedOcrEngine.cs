using iTerminal.Core.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.Documents;

/// <summary>
/// The OCR engine the application actually registers: the real one behind the
/// <c>DocumentsOcrEnabled</c> setting.
///
/// <para>A decorator rather than a check inside <see cref="Extractors.ImageTextExtractor"/>, because
/// "is OCR installed" and "does the user want OCR" are two different questions with two different
/// owners, and the extractor should only ever have to ask the first. It also keeps the extractor
/// testable without a settings store.</para>
///
/// <para>The setting is read on every call rather than captured. It is a cached in-memory lookup, a
/// scan asks once per image, and reading it live is what lets the switch take effect on the next
/// scan instead of the next restart.</para>
/// </summary>
public sealed class SettingsGatedOcrEngine(IOcrEngine? inner = null, ISettingsService? settingsService = null)
    : IOcrEngine
{
    private readonly IOcrEngine _inner = inner ?? new TesseractOcrEngine();
    private readonly ISettingsService _settingsService = settingsService ?? NullSettingsService.Instance;

    private bool IsEnabled => _settingsService.GetAsBool(Settings.DocumentsOcrEnabled) ?? false;

    /// <summary>
    /// Names the state a user can act on. "Off" and "not installed" both mean no text comes out of
    /// images, but only one of them is fixed in the settings dialog, so they must not read alike.
    /// </summary>
    public string DisplayName => !IsEnabled
        ? "OCR off (enable it in Settings)"
        : _inner.DisplayName;

    public bool IsAvailable => IsEnabled && _inner.IsAvailable;

    public string Recognize(string imagePath, CancellationToken cancellationToken = default) =>
        IsAvailable ? _inner.Recognize(imagePath, cancellationToken) : string.Empty;
}
