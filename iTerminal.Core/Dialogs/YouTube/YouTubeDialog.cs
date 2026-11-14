using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Services;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Services.YouTube;

namespace iTerminal.Core.Dialogs.YouTube;

/// <summary>
/// The YouTube client: a search to type, the videos it finds on the left and the details of the one under the cursor on the right.
/// Enter on the search runs it; Enter on a video (or F3) plays it in the video viewer, which draws the picture the same way it draws
/// a file; F2 opens the video's page in the browser.
///
/// <para>Nothing waits on the network here: a search and the stream lookup run on background tasks and park their outcome for the
/// next idle pass. Only the newest request may land, and closing cancels the one in flight.</para>
/// </summary>
public class YouTubeDialog : SplitPaneDialog
{
    internal const int SearchCount = 25;

    private enum OutcomeKind
    {
        Search,
        Play,
        Message,
    }

    private sealed record Outcome(int Generation, OutcomeKind Kind, IReadOnlyList<YouTubeVideo>? Videos = null, string? Url = null, YouTubeVideo? Video = null, string? Text = null, bool IsError = false);

    private sealed class PaneViewer(IThemeService themeService) : TextViewerControl(false, themeService)
    {
        public override void OnInitializeLayout()
        {
            Position placed = Position;
            base.OnInitializeLayout();
            Position = placed;
        }
    }

    private sealed class VideoListBox : ListBox<YouTubeVideo>
    {
        public VideoListBox(IListBoxModel<YouTubeVideo> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = (video, _) => new ListItem<YouTubeVideo>(video) { Value = " " + video.Title, IsEnabled = true };
        }
    }

    private readonly IYouTubeService _service;
    private readonly IFileLauncher _launcher;
    private readonly VideoFileViewerDialog _player;
    private readonly TextBox _query;
    private readonly ConcurrentObservableCollection<YouTubeVideo> _videos = new();
    private readonly VideoListBox _results;
    private readonly TextViewerControl _details;
    private readonly Lock _landGate = new();

    private CancellationTokenSource? _cancellation;
    private Outcome? _pending;
    private int _generation;
    private string _status = string.Empty;
    private bool _statusIsError;
    private int _shownIndex = -1;

    /// <summary>How the video viewer is shown. The running app shows it as a dialog; a test replaces it to see what would be shown.</summary>
    internal Func<VideoFileViewerDialog, Task> ShowPlayer { get; set; }

    public YouTubeDialog(
        IYouTubeService service,
        IFileLauncher launcher,
        VideoFileViewerDialog player,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(YouTubeDialog), themeService)
    {
        _service = service;
        _launcher = launcher;
        _player = player;
        ShowPlayer = viewer => ShowDialogAsync(viewer);
        Title = UiText.Get("Menu.YouTube", "YouTube");

        _query = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("YouTube.Term", "Search"),
            Placeholder = UiText.Get("YouTube.TermPlaceholder", "a title, a channel or words from a video"),
        };
        _results = new VideoListBox(new ListBoxModel<YouTubeVideo>(_videos), themeService)
        {
            EmptyText = UiText.Get("YouTube.EmptyResults", " Type a search above and press Enter."),
        };
        _details = new PaneViewer(themeService) { CanFocus = true };

        AddControl(_query);
        AddControl(_results);
        AddControl(_details);
    }

    #region Test seams

    internal TextBox QueryBox => _query;
    internal ListBox<YouTubeVideo> Results => _results;
    internal TextViewerControl Details => _details;
    internal string Status => _status;

    /// <summary>The request the dialog last started; nothing awaits it in the dialog, so a test waits on it instead of polling.</summary>
    internal Task RequestTask { get; private set; } = Task.CompletedTask;

    /// <summary>Applies the pending outcome now; the idle pass does the same in the running app.</summary>
    internal void ApplyPendingForTest() => OnIdle();

    #endregion

    public override void Close()
    {
        // Cancelled, not awaited: the request ends on its own, and a late outcome is dropped by its generation.
        _cancellation?.Cancel();
        NextGeneration();
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
            _query.Position = new Position(1, 1);
            _query.ViewSize = new Size(Math.Max(12, iw), 1);
            _query.ScrollableModel.ViewSize = _query.ViewSize;

            int paneHeight = Math.Max(1, ih - 5);
            int leftWidth = Math.Clamp(iw / 3, Math.Min(16, iw), 56);
            int rightWidth = Math.Max(1, iw - leftWidth - 1);
            _results.Position = new Position(1, 4);
            _results.ViewSize = new Size(leftWidth, paneHeight);
            _details.Position = new Position(1 + leftWidth + 1, 4);
            _details.ViewSize = new Size(rightWidth, paneHeight);
        }

        base.OnInitializeLayout();
        ShowDetails();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("YouTube.HintSearchOrPlay", "Search, or play the video under the cursor"));
        yield return ("F3", UiText.Get("YouTube.HintPlay", "Play the video under the cursor"));
        yield return ("F2", UiText.Get("YouTube.HintOpenPage", "Open the video's page in the browser"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Enter:
                    if (_query.IsFocused)
                    {
                        StartSearch();
                        return true;
                    }

                    if (_results.IsFocused)
                    {
                        Play();
                        return true;
                    }

                    break;
                case ConsoleKey.F3:
                    Play();
                    return true;
                case ConsoleKey.F2:
                    OpenInBrowser();
                    return true;
            }
        }

        bool handled = await base.HandleKeyEventAsync(keyEvent);
        StateHasChanged();
        return handled;
    }

    #region Actions

    /// <summary>Enter on the search: looks the words up on a background task.</summary>
    internal void StartSearch()
    {
        string term = _query.Text.Trim();
        if (term.Length == 0)
        {
            SetStatus(UiText.Get("YouTube.TypeTerm", "Type something to search for."), isError: true);
            return;
        }

        int generation = BeginRequest(UiText.Get("YouTube.Searching", "Searching YouTube…"));
        CancellationToken token = _cancellation!.Token;
        RequestTask = Task.Run(async () =>
        {
            try
            {
                IReadOnlyList<YouTubeVideo> videos = await _service.SearchAsync(term, SearchCount, token);
                Land(new Outcome(generation, OutcomeKind.Search, Videos: videos, Text: term));
            }
            catch (OperationCanceledException)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: UiText.Get("Msg.Cancelled", "Cancelled.")));
            }
            catch (Exception exception)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: exception.Message, IsError: true));
            }
        });
    }

    /// <summary>Enter on a result, or F3: asks for the stream of the video under the cursor, then plays it in the viewer.</summary>
    internal void Play()
    {
        if (SelectedVideo() is not { } video)
        {
            SetStatus(UiText.Get("YouTube.NothingToPlay", "There is no video to play yet. Search first."), isError: true);
            return;
        }

        int generation = BeginRequest(string.Format(UiText.Get("YouTube.Resolving", "Getting the stream for {0}…"), video.Title));
        CancellationToken token = _cancellation!.Token;
        RequestTask = Task.Run(async () =>
        {
            try
            {
                string url = await _service.ResolvePlaybackUrlAsync(video.Id, token);
                Land(new Outcome(generation, OutcomeKind.Play, Url: url, Video: video));
            }
            catch (OperationCanceledException)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: UiText.Get("Msg.Cancelled", "Cancelled.")));
            }
            catch (Exception exception)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: exception.Message, IsError: true));
            }
        });
    }

    /// <summary>F2: the video's page in the system's browser.</summary>
    internal void OpenInBrowser()
    {
        if (SelectedVideo() is not { } video)
        {
            SetStatus(UiText.Get("YouTube.NothingToPlay", "There is no video to play yet. Search first."), isError: true);
            return;
        }

        int generation = _generation;
        string url = video.PageUrl;
        RequestTask = Task.Run(async () =>
        {
            LaunchOutcome outcome = await _launcher.OpenUrlAsync(url);
            Land(new Outcome(generation, OutcomeKind.Message,
                Text: outcome.Started ? string.Format(UiText.Get("YouTube.Opened", "Opened {0} in the browser."), url) : outcome.Failure ?? UiText.Get("YouTube.OpenFailed", "The browser could not be started."),
                IsError: !outcome.Started));
        });
    }

    private int BeginRequest(string status)
    {
        // A request still running is replaced: cancelled, and its outcome dropped by the new generation.
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        SetStatus(status, isError: false);
        return NextGeneration();
    }

    private int NextGeneration()
    {
        lock (_landGate)
        {
            return ++_generation;
        }
    }

    private void Land(Outcome outcome)
    {
        // A cancelled request still reports back. Only the newest request may park its outcome, so a late "Cancelled." from
        // the one it replaced cannot overwrite the answer the newer one has just parked.
        lock (_landGate)
        {
            if (outcome.Generation == _generation)
            {
                _pending = outcome;
            }
        }
    }

    private int SelectedIndex() => (int)_results.ScrollableModel.CursorPosition.Y;

    private YouTubeVideo? SelectedVideo()
    {
        IReadOnlyList<IListItem<YouTubeVideo>> items = _results.ListModel.ListItems;
        int index = SelectedIndex();
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

    public override void OnIdle()
    {
        base.OnIdle();

        Outcome? outcome = Interlocked.Exchange(ref _pending, null);
        if (outcome is not null && outcome.Generation == _generation)
        {
            Apply(outcome);
        }

        // The details come from the list itself, so following the cursor costs nothing and needs no delay.
        if (SelectedIndex() != _shownIndex)
        {
            ShowDetails();
        }
    }

    private void Apply(Outcome outcome)
    {
        switch (outcome.Kind)
        {
            case OutcomeKind.Search:
                IReadOnlyList<YouTubeVideo> videos = outcome.Videos ?? [];
                _videos.ReplaceAll([.. videos]);
                _results.UpdateModel();
                _results.ScrollableModel.CursorPosition.Y = 0;
                _results.ScrollableModel.ScrollPosition.Y = 0;
                ShowDetails();
                SetStatus(
                    videos.Count == 0
                        ? string.Format(UiText.Get("YouTube.NothingFound", "Nothing found for \"{0}\"."), outcome.Text)
                        : string.Format(UiText.Get("YouTube.Found", "{0} videos found."), videos.Count),
                    isError: videos.Count == 0);
                break;
            case OutcomeKind.Play when outcome.Url is { } url && outcome.Video is { } video:
                SetStatus(string.Format(UiText.Get("YouTube.Playing", "Playing {0}."), video.Title), isError: false);
                _ = PlayAsync(url, video);
                break;
            default:
                SetStatus(outcome.Text ?? string.Empty, outcome.IsError);
                break;
        }
    }

    /// <summary>Hands the stream to the video viewer. The viewer decodes it with ffmpeg, as it does for a file.</summary>
    private async Task PlayAsync(string url, YouTubeVideo video)
    {
        try
        {
            _player.FilePath = url;
            _player.DisplayName = video.Title;
            await ShowPlayer(_player);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void ShowDetails()
    {
        _shownIndex = SelectedIndex();
        YouTubeVideo? video = SelectedVideo();
        List<string> lines = [];
        if (video is null)
        {
            lines.Add(UiText.Get("YouTube.NoDetails", "Search and pick a video to see its details."));
        }
        else
        {
            lines.Add(video.Title);
            lines.Add(new string('═', Math.Max(1, Math.Min(video.Title.Length, Math.Max(8, (int)_details.ViewSize.Width - 2)))));
            lines.Add(string.Empty);
            lines.Add(string.Format(UiText.Get("YouTube.DetailChannel", "Channel: {0}"), video.Channel.Length == 0 ? "—" : video.Channel));
            lines.Add(string.Format(UiText.Get("YouTube.DetailDuration", "Duration: {0}"), video.Duration is { } duration ? FormatDuration(duration) : "—"));
            lines.Add(string.Format(UiText.Get("YouTube.DetailViews", "Views: {0}"), video.Views?.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) ?? "—"));
            lines.Add(string.Format(UiText.Get("YouTube.DetailPublished", "Published: {0}"), video.Published?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.CurrentCulture) ?? "—"));
            lines.Add(string.Format(UiText.Get("YouTube.DetailLink", "Link: {0}"), video.PageUrl));
        }

        _details.TextModel.Lines = [.. lines];
        _details.ScrollableModel.ScrollableSize = new Size(lines.Max(line => line.Length), lines.Count);
        _details.ScrollableModel.ScrollPosition.Y = 0;
        _details.ScrollableModel.ScrollPosition.X = 0;
        _details.ScrollableModel.CursorPosition.Y = 0;
        _details.ScrollableModel.CursorPosition.X = 0;
        StateHasChanged();
    }

    internal static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
        StateHasChanged();
    }

    #endregion

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

        WriteLine(buffer, ix, iy + 1, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
        int leftWidth = (int)_results.ViewSize.Width;
        int rightWidth = (int)_details.ViewSize.Width;
        DrawSectionHeader(buffer, ix, iy + 2, leftWidth, string.Format(UiText.Get("YouTube.Videos", "Videos ({0})"), _videos.Count), _results.IsFocused, colorScheme);
        DrawSectionHeader(buffer, ix + leftWidth + 1, iy + 2, rightWidth, UiText.Get("YouTube.Details", "Details"), _details.IsFocused, colorScheme);

        string hints = string.Join("  ", GetKeyHints().Select(hint => $"{hint.Key}:{hint.Description}"));
        DrawStatusAndHints(buffer, ix, iy + ih - 1, iw, _status, _statusIsError ? colorScheme.Error : Color.Cyan, hints, colorScheme);
    }
}
