using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Prompts;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Evidence;
using iTerminal.Core.Speech;
using iConsole.Media.Speech;

namespace iTerminal.Core.Commands.Evidence;

/// <summary>
/// The folder prompt and the open step, shared by the open command and by the add command, which opens
/// a notebook first when none is open. The folder is remembered for the next time as a plain setting,
/// so the prompt starts there. The setting is not the notebook: the notebook is whatever index is in that folder.
/// </summary>
public static class EvidenceNotebookOpening
{
    /// <summary>The setting that remembers the last notebook folder.</summary>
    public const string FolderSettingKey = "EvidenceNotebook.Folder";

    /// <summary>Opens a notebook when none is open; asks for the folder first. False when the person cancels or it cannot be opened.</summary>
    public static async Task<bool> EnsureOpenAsync(IPromptService prompts, ISettingsService settings, IEvidenceNotebookService notebook, INotificationService? notifications)
    {
        if (notebook.IsOpen)
        {
            return true;
        }

        return await ChooseAndOpenAsync(prompts, settings, notebook, notifications);
    }

    /// <summary>Asks for a folder, opens the notebook there (creating it when new) and remembers the folder. False when cancelled or refused.</summary>
    public static async Task<bool> ChooseAndOpenAsync(IPromptService prompts, ISettingsService settings, IEvidenceNotebookService notebook, INotificationService? notifications)
    {
        var remembered = notebook.FolderPath ?? settings.Get(FolderSettingKey) ?? string.Empty;
        var prompt = new TextPrompt<string>(UiText.Get("Evidence.Open.Question", "Folder for the evidence notebook (created if it does not exist)"))
        {
            DefaultValue = remembered,
        };
        prompt.Validate(text => Resolve(text) is null
            ? UiText.Get("Evidence.Open.BadFolder", "Enter a folder path, such as ~/cases/case-42")
            : null);

        var answer = await prompts.AskAsync(prompt);
        if (answer is null || Resolve(answer) is not { } folder)
        {
            return false;
        }

        try
        {
            await notebook.OpenAsync(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            notifications?.Notify(UiText.Get("Evidence.Open.Failed", "The notebook did not open"), exception.Message, NotificationSeverity.Error);
            return false;
        }

        settings.Set(FolderSettingKey, folder);
        settings.Write();
        notifications?.Notify(UiText.Get("Evidence.Open.Opened", "Evidence notebook open"), folder, NotificationSeverity.Success);
        return true;
    }

    /// <summary>The full path the typed text names, with a leading <c>~</c> taken as the home folder; null when it names nothing usable.</summary>
    internal static string? Resolve(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.StartsWith('~'))
        {
            trimmed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), trimmed[1..].TrimStart('/', '\\'));
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

/// <summary>
/// Ctrl+Shift+Q: shows the evidence notebook in the focused panel, choosing its folder first. Pressed on
/// the notebook it goes back to where the panel came from, like the other drive keys.
/// </summary>
public class OpenEvidenceNotebookCommand(
    IContentModelService contentModelService,
    IEvidenceNotebookService notebook,
    IPromptService promptService,
    ISettingsService settingsService,
    INotificationService? notificationService = null) : Command
{
    public override string Title => "Evidence notebook";

    public override string Description => "Show the evidence notebook in the focused panel, choosing the folder it is kept in. Press again to go back.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.Q, true, false, true);

    // A second press while the folder question is up is the same question, not a second one.
    private int _asking;

    /// <remarks>
    /// The folder question is not awaited on the key handler: the keyboard loop runs the handler to completion before it
    /// reads the next key, and the prompt can only be answered through that loop. The answer is awaited in the background
    /// instead, and the panel changes when it lands.
    /// </remarks>
    public override Task ExecuteAsync(object? args)
    {
        var panel = contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();
        if (panel is null)
        {
            return Task.CompletedTask;
        }

        if (panel.Url.ContainerType == ContainerType.EvidenceNotebook && panel is ContentModelAsyncDrive drive)
        {
            return contentModelService.SetUrlAsync(panel.Name, drive.ReturnUrl);
        }

        if (Interlocked.Exchange(ref _asking, 1) == 0)
        {
            _ = AskThenShowAsync(panel.Name);
        }

        return Task.CompletedTask;
    }

    private async Task AskThenShowAsync(string panelName)
    {
        try
        {
            if (!await EvidenceNotebookOpening.ChooseAndOpenAsync(promptService, settingsService, notebook, notificationService))
            {
                return;
            }

            await contentModelService.SetUrlAsync(panelName, new Url(Container.EVIDENCE));
        }
        finally
        {
            Interlocked.Exchange(ref _asking, 0);
        }
    }
}

/// <summary>
/// Ctrl+Shift+Delete: adds evidence to the open notebook. The other panel supplies files (the marked ones, or
/// the one under the cursor); the rest is typed, spoken or copied. Every entry records its origin as it is
/// added, so the person says only what the evidence is.
/// </summary>
public class AddEvidenceCommand(
    IContentModelService contentModelService,
    IEvidenceNotebookService notebook,
    IPromptService promptService,
    ISettingsService settingsService,
    ITextClipboardService clipboard,
    IMicrophoneCaptureFactory microphoneCaptureFactory,
    FileSystemResolver fileSystemResolver,
    INotificationService? notificationService = null) : Command
{
    public override string Title => "Add evidence";

    public override string Description => "Add to the evidence notebook: files from the other panel, a note, a voice recording, clipboard text or a web address.";

    public override Key? Key { get; init; } = new Key(ConsoleKey.Delete, true, false, true);

    /// <summary>What the add prompt offers.</summary>
    public enum Choice
    {
        Files,
        Note,
        Voice,
        Clipboard,
        Link,
    }

    // A second press while the add question is up is the same question, not a second one.
    private int _asking;

    /// <remarks>
    /// Like the open command, the questions are awaited in the background: the key handler returns at once and the
    /// keyboard loop goes on to read the answer.
    /// </remarks>
    public override Task ExecuteAsync(object? args)
    {
        if (Interlocked.Exchange(ref _asking, 1) == 0)
        {
            _ = AskAndAddAsync();
        }

        return Task.CompletedTask;
    }

    private async Task AskAndAddAsync()
    {
        try
        {
            await AskAndAddCoreAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _asking, 0);
        }
    }

    private async Task AskAndAddCoreAsync()
    {
        if (!await EvidenceNotebookOpening.EnsureOpenAsync(promptService, settingsService, notebook, notificationService))
        {
            return;
        }

        // A nullable choice type, so a cancelled prompt (null) is not mistaken for the first choice.
        Choice?[] options = [Choice.Files, Choice.Note, Choice.Voice, Choice.Clipboard, Choice.Link];
        var choice = await promptService.SelectAsync(new SelectionPrompt<Choice?>(
            UiText.Get("Evidence.Add.Question", "Add what to the evidence notebook?"),
            options)
        {
            Label = option => ChoiceLabel(option!.Value),
        });
        if (choice is null)
        {
            return;
        }

        switch (choice.Value)
        {
            case Choice.Files:
                AddFiles();
                break;
            case Choice.Note:
                await AddNoteAsync();
                break;
            case Choice.Voice:
                await RecordVoiceAsync();
                break;
            case Choice.Clipboard:
                await AddClipboardAsync();
                break;
            case Choice.Link:
                await AddLinkAsync();
                break;
        }
    }

    private static string ChoiceLabel(Choice choice) => choice switch
    {
        Choice.Files => UiText.Get("Evidence.Add.Files", "Files from the other panel"),
        Choice.Note => UiText.Get("Evidence.Add.Note", "A note"),
        Choice.Voice => UiText.Get("Evidence.Add.Voice", "A voice recording"),
        Choice.Clipboard => UiText.Get("Evidence.Add.Clipboard", "Text from the clipboard"),
        _ => UiText.Get("Evidence.Add.Link", "A web address"),
    };

    /// <summary>The panel files are taken from: the focused one, unless that is the notebook; otherwise the other one.</summary>
    internal IContentModel? SourceModel()
    {
        var focused = contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();
        if (focused is not null && focused.Url.ContainerType != ContainerType.EvidenceNotebook)
        {
            return focused;
        }

        return contentModelService.ContentModels.FirstOrDefault(model =>
            !ReferenceEquals(model, focused) && model.Url.ContainerType != ContainerType.EvidenceNotebook);
    }

    /// <summary>
    /// Copies the actionable files of the source panel into the notebook on a background task, so the key
    /// handler returns at once. The notebook panel is refreshed when they have all been copied.
    /// </summary>
    private void AddFiles()
    {
        var source = SourceModel();
        if (source is null)
        {
            Notify(UiText.Get("Evidence.Add.NoSource", "No other panel to take files from"), null, NotificationSeverity.Warning);
            return;
        }

        var urls = FocusedEntry.ActionableUrls(source);
        if (urls.Count == 0)
        {
            Notify(UiText.Get("Evidence.Add.NoFiles", "Nothing to add: mark files, or put the cursor on one"), null, NotificationSeverity.Warning);
            return;
        }

        _ = Task.Run(async () =>
        {
            int added = 0;
            int folders = 0;
            int untimestamped = 0;
            string? failure = null;
            try
            {
                foreach (var url in urls)
                {
                    if (url.PathType != PathType.File)
                    {
                        folders++;
                        continue;
                    }

                    try
                    {
                        // The same resolution a copy uses: a panel model cannot stream its own files.
                        var sourceFileSystem = fileSystemResolver.Resolve(url, source, "Add evidence");
                        var (size, modifiedUtc) = SourceFacts(source, url);
                        var record = await notebook.AddFileAsync(sourceFileSystem, url, size, modifiedUtc);
                        added++;
                        if (record.Timestamp is null)
                        {
                            untimestamped++;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or FileNotFoundException or InvalidOperationException)
                    {
                        failure ??= $"{url.Name}: {exception.Message}";
                    }
                }
            }
            catch (Exception exception)
            {
                // Nothing here may end the task silently: the person would wait for a result that never comes.
                failure ??= exception.Message;
            }

            var message = string.Format(UiText.Get("Evidence.Add.Added", "Added {0} to the notebook"), added);
            if (folders > 0)
            {
                message += string.Format(UiText.Get("Evidence.Add.Folders", "; {0} folder(s) skipped: a folder is not evidence on its own"), folders);
            }

            if (untimestamped > 0)
            {
                message += string.Format(UiText.Get("Evidence.Add.Untimestamped", "; {0} without an RFC 3161 time stamp"), untimestamped);
            }

            RefreshNotebookPanels();
            if (failure is not null)
            {
                Notify(message, failure, NotificationSeverity.Error);
            }
            else
            {
                Notify(message, null, folders > 0 || untimestamped > 0 ? NotificationSeverity.Warning : NotificationSeverity.Success);
            }
        });
    }

    private static (long? Size, DateTime? ModifiedUtc) SourceFacts(IContentModel source, Url url)
    {
        var entry = source.Entries.FirstOrDefault(candidate => string.Equals(candidate.Url.FullUrl, url.FullUrl, StringComparison.Ordinal));
        if (entry is VirtualDriveEntry virtualEntry)
        {
            return (virtualEntry.Size, virtualEntry.Modified?.ToUniversalTime());
        }

        if (url.ContainerType == ContainerType.FileSystem && File.Exists(url.FullUrl))
        {
            var info = new FileInfo(url.FullUrl);
            return (info.Length, info.LastWriteTimeUtc);
        }

        return (null, null);
    }

    private async Task AddNoteAsync()
    {
        var answer = await promptService.AskAsync(new TextPrompt<string>(UiText.Get("Evidence.Note.Question", "Note")));
        if (answer is null)
        {
            return;
        }

        await AddTextAsync(EvidenceKind.Note, answer, UiText.Get("Evidence.Note.Added", "Note added"));
    }

    private async Task AddLinkAsync()
    {
        var prompt = new TextPrompt<string>(UiText.Get("Evidence.Link.Question", "Web address"));
        prompt.Validate(text => Uri.TryCreate(text.Trim(), UriKind.Absolute, out _)
            ? null
            : UiText.Get("Evidence.Link.Invalid", "Enter a web address such as https://example.com"));

        var answer = await promptService.AskAsync(prompt);
        if (answer is null)
        {
            return;
        }

        await AddTextAsync(EvidenceKind.Link, answer.Trim(), UiText.Get("Evidence.Link.Added", "Web address added"));
    }

    private async Task AddClipboardAsync()
    {
        var text = await clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            Notify(UiText.Get("Evidence.Clipboard.Empty", "The clipboard holds no text"), null, NotificationSeverity.Warning);
            return;
        }

        await AddTextAsync(EvidenceKind.Clipboard, text, UiText.Get("Evidence.Clipboard.Added", "Clipboard text added"));
    }

    private async Task AddTextAsync(EvidenceKind kind, string text, string doneTitle)
    {
        try
        {
            var record = await notebook.AddTextAsync(kind, text);
            RefreshNotebookPanels();
            if (record.Timestamp is null)
            {
                Notify(doneTitle, UiText.Get("Evidence.Add.NoTimestampNote", "No RFC 3161 time stamp was obtained; the entry is kept, and Verify reports it."), NotificationSeverity.Warning);
            }
            else
            {
                Notify(doneTitle, null, NotificationSeverity.Success);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Notify(UiText.Get("Evidence.Add.Failed", "The entry was not added"), exception.Message, NotificationSeverity.Error);
        }
    }

    /// <summary>
    /// Records from the microphone until the person answers the prompt: Enter keeps the recording, Esc discards
    /// it. The capture starts before the prompt and is stopped and released in every case.
    /// </summary>
    private async Task RecordVoiceAsync()
    {
        IMicrophoneCapture capture;
        try
        {
            capture = microphoneCaptureFactory.Create();
        }
        catch (iConsole.Media.Speech.SpeechException exception)
        {
            Notify(UiText.Get("Evidence.Voice.Unavailable", "No recording"), exception.Message, NotificationSeverity.Error);
            return;
        }

        using (capture)
        {
            try
            {
                await Task.Run(capture.Start);
            }
            catch (iConsole.Media.Speech.SpeechException exception)
            {
                Notify(UiText.Get("Evidence.Voice.Unavailable", "No recording"), exception.Message, NotificationSeverity.Error);
                return;
            }

            var since = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            var keep = await promptService.AskAsync(new TextPrompt<string>(string.Format(
                UiText.Get("Evidence.Voice.Question", "Recording since {0}. Enter keeps it, Esc discards it."), since))
            {
                AllowEmpty = true,
            });

            var pcm = await Task.Run(capture.Stop);
            if (keep is null)
            {
                Notify(UiText.Get("Evidence.Voice.Discarded", "Recording discarded"), null, NotificationSeverity.Info);
                return;
            }

            if (pcm.Length < Pcm.MinBytes)
            {
                Notify(UiText.Get("Evidence.Voice.TooShort", "The recording was too short to keep"), null, NotificationSeverity.Warning);
                return;
            }

            try
            {
                var record = await notebook.AddVoiceAsync(pcm, Pcm.SampleRate);
                RefreshNotebookPanels();
                if (record.Timestamp is null)
                {
                    Notify(UiText.Get("Evidence.Voice.Added", "Voice recording added"), UiText.Get("Evidence.Add.NoTimestampNote", "No RFC 3161 time stamp was obtained; the entry is kept, and Verify reports it."), NotificationSeverity.Warning);
                }
                else
                {
                    Notify(UiText.Get("Evidence.Voice.Added", "Voice recording added"), null, NotificationSeverity.Success);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Notify(UiText.Get("Evidence.Add.Failed", "The entry was not added"), exception.Message, NotificationSeverity.Error);
            }
        }
    }

    private void RefreshNotebookPanels()
    {
        foreach (var model in contentModelService.ContentModels.Where(model => model.Url.ContainerType == ContainerType.EvidenceNotebook))
        {
            model.Refresh(forceRefresh: true);
        }
    }

    private void Notify(string title, string? message, NotificationSeverity severity) =>
        notificationService?.Notify(title, message, severity);
}

/// <summary>
/// Checks every entry of the open notebook against its recorded digest and size, and reports what does
/// not match. Changes nothing. Found in the command palette; it has no hot key.
/// </summary>
public class VerifyEvidenceNotebookCommand(IEvidenceNotebookService notebook, INotificationService? notificationService = null) : Command
{
    public override string Title => "Verify evidence notebook";

    public override string Description => "Check that every entry in the evidence notebook still matches its recorded SHA-256 and size.";

    public override async Task ExecuteAsync(object? args)
    {
        if (!notebook.IsOpen)
        {
            notificationService?.Notify(UiText.Get("Evidence.Verify.NoNotebook", "No evidence notebook is open"), null, NotificationSeverity.Warning);
            return;
        }

        var entries = await notebook.ListAsync();
        var problems = await notebook.VerifyAsync();
        if (problems.Count == 0)
        {
            notificationService?.Notify(
                string.Format(UiText.Get("Evidence.Verify.Clean", "All {0} entries match their records"), entries.Count),
                null,
                NotificationSeverity.Success);
            return;
        }

        notificationService?.Notify(
            string.Format(UiText.Get("Evidence.Verify.Problems", "{0} of {1} entries do not match"), problems.Count, entries.Count),
            $"{problems[0].FileName}: {problems[0].Message}",
            NotificationSeverity.Error);
    }
}
