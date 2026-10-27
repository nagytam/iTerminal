using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Translation;
using System.Text;

namespace iTerminal.Core.Dialogs.Translator;

/// <summary>
/// The translator: a text to type or paste, a language to translate it from (or detect) and into, and an
/// engine — the translation API, the connected AI model, or whichever is set up (see
/// <see cref="TranslationEngine"/>). F5 translates, F7 detects the language, F6 swaps the languages and
/// brings the translation back into the text, F8 clears.
///
/// <para>Nothing here waits on the network: a request runs on a background task and its outcome is parked
/// until the next idle pass applies it on the UI thread. Only the newest request may land, and closing the
/// dialog cancels the one in flight.</para>
/// </summary>
public class TranslatorDialog : SplitPaneDialog
{
    internal const string KeyHints = "F5:translate  F6:swap  F7:detect  F8:clear  Tab:next field  Esc:close";

    internal const string SourceKey = "Translator.Source";
    internal const string TargetKey = "Translator.Target";
    internal const string EngineKey = "Translator.Engine";

    // The text viewer centres itself in its parent on every layout, whatever position it was given — right for a
    // viewer that fills its dialog, wrong for two side by side. These keep the position they were placed at.
    private sealed class PaneEditor(IThemeService themeService) : TextEditorControl(themeService)
    {
        public override void OnInitializeLayout()
        {
            Position placed = Position;
            base.OnInitializeLayout();
            Position = placed;
        }

        // Text to translate has no use for indentation, and the editor took Tab for it: focus could never leave the
        // pane, so the translation could not be reached or scrolled from the keyboard. Tab moves on, as the footer says.
        public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.Tab && !keyEvent.Control && !keyEvent.Alt)
            {
                return false;
            }

            return await base.HandleKeyEventAsync(keyEvent);
        }
    }

    private sealed class PaneViewer(IThemeService themeService) : TextViewerControl(false, themeService)
    {
        public override void OnInitializeLayout()
        {
            Position placed = Position;
            base.OnInitializeLayout();
            Position = placed;
        }
    }

    private static readonly TranslationLanguage DetectChoice = new(TranslationLanguages.Auto, "Detect language");

    internal sealed record EngineChoice(TranslationEngine Engine, string Name);

    /// <summary>What a finished request leaves for the next idle pass: a translation, a detection, or a failure.</summary>
    private sealed record Outcome(int Generation, string Status, bool IsError, string? Translation = null, string? DetectedSource = null, string? Engine = null);

    private static readonly EngineChoice[] EngineChoices =
    [
        new(TranslationEngine.Auto, "Automatic"),
        new(TranslationEngine.Api, "Translation API"),
        new(TranslationEngine.Llm, "AI model"),
    ];

    private readonly ITranslationService _service;
    private readonly ISettingsService _settings;
    private readonly TranslationOptions _options;
    private readonly ComboBox<TranslationLanguage> _from;
    private readonly ComboBox<TranslationLanguage> _to;
    private readonly ComboBox<EngineChoice> _engine;
    private readonly TextEditorControl _input;
    private readonly TextViewerControl _output;

    private CancellationTokenSource? _cancellation;
    private Outcome? _pending;
    private int _generation;
    private string _translation = string.Empty;
    private string _status = string.Empty;
    private bool _statusIsError;
    private string? _detectedSource;
    private volatile bool _busy;

    public TranslatorDialog(
        ITranslationService service,
        ISettingsService settings,
        Microsoft.Extensions.Options.IOptions<TranslationOptions> options,
        IThemeService themeService)
        : base(nameof(TranslatorDialog), themeService)
    {
        _service = service;
        _settings = settings;
        _options = options.Value;
        Title = "Translator";

        _from = new ComboBox<TranslationLanguage>(themeService) { Label = "From", DisplaySelector = language => language.Name, MaxDropDownRows = 10 };
        _to = new ComboBox<TranslationLanguage>(themeService) { Label = "To", DisplaySelector = language => language.Name, MaxDropDownRows = 10 };
        _engine = new ComboBox<EngineChoice>(themeService) { Label = "Engine", DisplaySelector = choice => choice.Name };
        // Neither is focusable by default, which left Tab cycling through the three pickers only.
        _input = new PaneEditor(themeService) { CanFocus = true };
        _output = new PaneViewer(themeService) { CanFocus = true };


        _from.SetItems([DetectChoice, .. TranslationLanguages.All]);
        _to.SetItems(TranslationLanguages.All);
        _engine.SetItems(EngineChoices);
        _from.SelectionChanged += (_, _) => _detectedSource = null;

        AddControl(_from);
        AddControl(_to);
        AddControl(_engine);
        AddControl(_input);
        AddControl(_output);
    }

    #region Test seams

    internal ComboBox<TranslationLanguage> FromBox => _from;
    internal ComboBox<TranslationLanguage> ToBox => _to;
    internal ComboBox<EngineChoice> EngineBox => _engine;
    internal TextEditorControl Input => _input;
    internal TextViewerControl Output => _output;
    internal string Status => _status;
    internal bool IsBusy => _busy;

    /// <summary>The request the dialog last started; nothing awaits it in the dialog, so a test waits on it instead of polling.</summary>
    internal Task RequestTask { get; private set; } = Task.CompletedTask;

    internal string InputText
    {
        get => string.Join('\n', _input.TextModel.Lines.AsSnapshot());
        set
        {
            _input.TextModel.Lines = [.. value.Split('\n')];
            _input.Refresh();
        }
    }

    internal string TranslationText => _translation;

    #endregion

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        SelectLanguage(_from, _settings.Get(SourceKey), DetectChoice.Code);
        SelectLanguage(_to, _settings.Get(TargetKey), TranslationLanguages.Find(_options.DefaultTargetLanguage)?.Code ?? "en");
        int engineIndex = Array.FindIndex(EngineChoices, choice => choice.Engine.ToString() == _settings.Get(EngineKey));
        _engine.SelectedIndex = Math.Max(0, engineIndex);

        _detectedSource = null;
        _status = _service.UnavailableReason(SelectedEngine) is { } reason ? reason : string.Empty;
        _statusIsError = _status.Length > 0;
    }

    private static void SelectLanguage(ComboBox<TranslationLanguage> box, string? code, string fallback)
    {
        int index = box.Items.ToList().FindIndex(language => language.Code == code);
        if (index < 0)
        {
            index = box.Items.ToList().FindIndex(language => language.Code == fallback);
        }

        box.SelectedIndex = Math.Max(0, index);
    }

    private TranslationEngine SelectedEngine => _engine.SelectedItem?.Engine ?? TranslationEngine.Auto;

    public override void Close()
    {
        _settings.Set(SourceKey, _from.SelectedItem?.Code ?? DetectChoice.Code);
        _settings.Set(TargetKey, _to.SelectedItem?.Code ?? "en");
        _settings.Set(EngineKey, SelectedEngine.ToString());

        // Cancelled, not awaited: the request ends on its own, and a late outcome is dropped by its generation.
        _cancellation?.Cancel();
        _generation++;
        base.Close();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw > 0 && ih > 0)
        {
            int boxWidth = Math.Max(10, (iw - 2) / 3);
            _from.Position = new Position(1, 1);
            _from.ViewSize = new Size(boxWidth, 1);
            _to.Position = new Position(1 + boxWidth + 1, 1);
            _to.ViewSize = new Size(boxWidth, 1);
            _engine.Position = new Position(1 + (boxWidth + 1) * 2, 1);
            _engine.ViewSize = new Size(Math.Max(10, iw - (boxWidth + 1) * 2), 1);

            int paneHeight = Math.Max(1, ih - 5);
            int leftWidth = Math.Max(1, (iw - 1) / 2);
            int rightWidth = Math.Max(1, iw - leftWidth - 1);
            _input.Position = new Position(1, 4);
            _input.ViewSize = new Size(leftWidth, paneHeight);
            _output.Position = new Position(1 + leftWidth + 1, 4);
            _output.ViewSize = new Size(rightWidth, paneHeight);
        }

        base.OnInitializeLayout();
        ShowTranslation();
    }

    /// <summary>True after the first F8 over a text: the second one clears; any other key withdraws it.</summary>
    private bool _clearArmed;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("F5", "Translate");
        yield return ("F6", "Swap the two languages");
        yield return ("F7", "Detect the language of the text");
        yield return ("F8", "Clear the text (asks first)");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && keyEvent.ConsoleKey != ConsoleKey.F8)
        {
            _clearArmed = false;
        }

        if (keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift && !_from.IsOpen && !_to.IsOpen && !_engine.IsOpen)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.F5:
                    StartTranslate();
                    return true;
                case ConsoleKey.F6:
                    SwapLanguages();
                    return true;
                case ConsoleKey.F7:
                    StartDetect();
                    return true;
                case ConsoleKey.F8:
                    if (InputText.Length > 0 && !_clearArmed)
                    {
                        // A text that was typed or pasted is gone for good: the key asks once.
                        _clearArmed = true;
                        SetStatus("Press F8 again to clear the text, any other key to keep it.", isError: false);
                        return true;
                    }

                    Clear();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    #region Actions

    /// <summary>F5: translates the text with the chosen languages and engine, on a background task.</summary>
    internal void StartTranslate()
    {
        string text = InputText;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Type or paste a text to translate.", isError: true);
            return;
        }

        string source = _from.SelectedItem?.Code ?? TranslationLanguages.Auto;
        string target = _to.SelectedItem?.Code ?? "en";
        TranslationEngine engine = SelectedEngine;
        int generation = BeginRequest($"Translating with {EngineName(engine)}…");
        CancellationToken token = _cancellation!.Token;

        RequestTask = Task.Run(async () =>
        {
            try
            {
                TranslationResult result = await _service.TranslateAsync(text, source, target, engine, token);
                string from = source == TranslationLanguages.Auto
                    ? (result.DetectedSource is { } detected ? $"{TranslationLanguages.NameOf(detected)} (detected)" : "unknown language")
                    : TranslationLanguages.NameOf(source);
                Land(new Outcome(generation, $"{from} → {TranslationLanguages.NameOf(target)} · {result.EngineName}", false, result.Text, result.DetectedSource, result.EngineName));
            }
            catch (OperationCanceledException)
            {
                Land(new Outcome(generation, "Cancelled.", false));
            }
            catch (TranslationException exception)
            {
                Land(new Outcome(generation, exception.Message, true));
            }
            catch (Exception exception)
            {
                Land(new Outcome(generation, $"Translation failed: {exception.Message}", true));
            }
        });
    }

    /// <summary>F7: finds the language of the text — offline when that is sure, otherwise by asking the engine.</summary>
    internal void StartDetect()
    {
        string text = InputText;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Type or paste a text to detect its language.", isError: true);
            return;
        }

        TranslationEngine engine = SelectedEngine;
        int generation = BeginRequest("Detecting the language…");
        CancellationToken token = _cancellation!.Token;

        RequestTask = Task.Run(async () =>
        {
            try
            {
                LanguageDetection? detection = await _service.DetectAsync(text, engine, token);
                Land(detection is null
                    ? new Outcome(generation, "Could not tell the language — choose it from the From list.", true)
                    : new Outcome(generation, $"Detected {TranslationLanguages.NameOf(detection.Code)} ({detection.Confidence:P0} sure)", false, DetectedSource: detection.Code));
            }
            catch (OperationCanceledException)
            {
                Land(new Outcome(generation, "Cancelled.", false));
            }
            catch (Exception exception)
            {
                Land(new Outcome(generation, $"Detection failed: {exception.Message}", true));
            }
        });
    }

    /// <summary>F6: swaps the languages and makes the translation the new text, so a translation can be checked by translating it back.</summary>
    internal void SwapLanguages()
    {
        // Detect-language has no target to swap in: the language found for the text, or the offline guess, stands in.
        string? source = _from.SelectedItem?.Code is { } chosen && chosen != TranslationLanguages.Auto
            ? chosen
            : _detectedSource ?? _service.DetectOffline(InputText)?.Code;
        string? target = _to.SelectedItem?.Code;
        if (source is null || TranslationLanguages.Find(source) is null)
        {
            SetStatus("Choose the From language first — it is needed to swap.", isError: true);
            return;
        }

        SelectLanguage(_from, target, DetectChoice.Code);
        SelectLanguage(_to, source, "en");
        if (_translation.Length > 0)
        {
            InputText = _translation;
        }

        _translation = string.Empty;
        _detectedSource = null;
        ShowTranslation();
        SetStatus(string.Empty, isError: false);
    }

    /// <summary>F8: empties both texts.</summary>
    internal void Clear()
    {
        _clearArmed = false;
        _cancellation?.Cancel();
        _generation++;
        _busy = false;
        InputText = string.Empty;
        _translation = string.Empty;
        _detectedSource = null;
        ShowTranslation();
        SetStatus(string.Empty, isError: false);
        _input.SetFocus();
    }

    private int BeginRequest(string status)
    {
        // A request still running is replaced: cancelled, and its outcome dropped by the new generation.
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        _busy = true;
        SetStatus(status, isError: false);
        return ++_generation;
    }

    private void Land(Outcome outcome) => _pending = outcome;

    #endregion

    #region Presentation

    public override void OnIdle()
    {
        base.OnIdle();

        Outcome? outcome = Interlocked.Exchange(ref _pending, null);
        if (outcome is null || outcome.Generation != _generation)
        {
            return;
        }

        _busy = false;
        if (outcome.Translation is not null)
        {
            _translation = outcome.Translation;
            ShowTranslation();
        }

        if (outcome.DetectedSource is not null)
        {
            _detectedSource = outcome.DetectedSource;
        }

        SetStatus(outcome.Status, outcome.IsError);
    }

    /// <summary>Applies the pending outcome now; the idle pass does the same in the running app.</summary>
    internal void ApplyPendingForTest() => OnIdle();

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
        StateHasChanged();
    }

    private static string EngineName(TranslationEngine engine) => EngineChoices.First(choice => choice.Engine == engine).Name.ToLowerInvariant();

    /// <summary>Puts the translation in the right-hand pane, wrapped to the pane's width — the viewer only scrolls sideways otherwise.</summary>
    private void ShowTranslation()
    {
        int width = Math.Max(8, (int)_output.ViewSize.Width - 2);
        List<string> lines = Wrap(_translation, width);
        _output.TextModel.Lines = [.. lines];
        _output.ScrollableModel.ScrollableSize = new Size(lines.Count == 0 ? 0 : lines.Max(line => line.Length), lines.Count);
        _output.ScrollableModel.ScrollPosition.Y = 0;
        _output.ScrollableModel.ScrollPosition.X = 0;
        _output.ScrollableModel.CursorPosition.Y = 0;
        _output.ScrollableModel.CursorPosition.X = 0;
        StateHasChanged();
    }

    /// <summary>Breaks each line at a space where it can, and at the width where there is none (Chinese and Japanese have no spaces).</summary>
    internal static List<string> Wrap(string text, int width)
    {
        List<string> lines = [];
        if (text.Length == 0)
        {
            return lines;
        }

        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            string rest = paragraph;
            while (rest.Length > width)
            {
                int cut = rest.LastIndexOf(' ', width);
                if (cut <= 0)
                {
                    cut = width;
                }

                lines.Add(rest[..cut].TrimEnd());
                rest = rest[cut..].TrimStart();
            }

            lines.Add(rest);
        }

        return lines;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int ix = (int)AbsolutePosition.X + 1;
        int iy = (int)AbsolutePosition.Y + 1;
        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        WriteLine(buffer, ix, iy + 1, iw, InfoLine(), colorScheme.Foreground, colorScheme.Background);

        int leftWidth = (int)_input.ViewSize.Width;
        int rightWidth = (int)_output.ViewSize.Width;
        DrawSectionHeader(buffer, ix, iy + 2, leftWidth, "Text", _input.IsFocused, colorScheme);
        DrawSectionHeader(buffer, ix + leftWidth + 1, iy + 2, rightWidth, "Translation", _output.IsFocused, colorScheme);

        DrawStatusAndHints(buffer, ix, iy + ih - 1, iw, _status, _statusIsError ? colorScheme.Error : Color.Cyan, KeyHints, colorScheme);
    }

    /// <summary>The line under the pickers: which language the text was found to be in, or why the chosen engine cannot be used.</summary>
    internal string InfoLine()
    {
        if (_service.UnavailableReason(SelectedEngine) is { } reason)
        {
            return $" {EngineName(SelectedEngine)} is not available: {reason}";
        }

        if (_from.SelectedItem?.Code == TranslationLanguages.Auto)
        {
            // Drawn on every repaint, so only the start of the text is looked at.
            LanguageDetection? offline = _detectedSource is null
                ? _service.DetectOffline(string.Join('\n', _input.TextModel.Lines.AsSnapshot().Take(40)))
                : null;
            string? code = _detectedSource ?? offline?.Code;
            return code is null
                ? " The source language is detected when you translate."
                : $" Looks like {TranslationLanguages.NameOf(code)}{(_detectedSource is null ? " (a guess)" : string.Empty)}.";
        }

        return string.Empty;
    }

    #endregion
}
