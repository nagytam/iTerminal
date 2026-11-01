### The Git client (`iTerminal.Core/Dialogs/Git/`, `Services/Git/`)
Ctrl+G opens `GitClientDialog`, a `SplitPaneDialog`: five stacked list sections on the left
(Unstaged / Staged / Branches / Commits / Stashes) and a detail pane on the right. `IGitService` is a
structured async wrapper over the `git` CLI — porcelain v1 `-z`, `ArgumentList` so nothing is quoted
by hand, a `SemaphoreSlim` write-lock so two mutating commands cannot collide on `index.lock`, and
`GIT_TERMINAL_PROMPT=0` so a push never hangs waiting for a credential prompt that has no TTY to
appear on. `GitLens` shares the same service.

- **Nothing runs git from a key handler or `Draw`.** Every command goes through `RunAction`, which
  starts a background task and returns; `Draw` reads cached fields and `Dialog.OnIdle` forces the
  repaint. `PendingOperation` is the task the most recent refresh or command started — nothing in the
  dialog awaits it, it exists so a test can wait for the work it just asked for instead of polling.
- **Section heights come from the content, not from a fifth each.** `DistributeSectionRows` gives
  every section one header row, then hands out the remaining rows round-robin to whichever sections
  still want more, so a long list cannot crowd out a short one. An empty section collapses to its
  header — a clean staging area and an empty stash list are worth ten rows to the file list in a
  repository with thirty changes. The section under the cursor is served first and always keeps at
  least one row, so it can say `(nothing staged)` rather than looking like a drawing glitch. Layout is
  therefore recomputed on every refresh and every focus change, not only at start-up.
- **`_activeSection` is what the detail pane follows, and it is not "the focused control".** The diff
  pane is itself focusable — a long patch has to be readable with the arrow keys — so tabbing into it
  must not blank the patch being read. `SyncActiveSection` adopts a section only when a *section*
  takes focus, and it runs from `OnIdle` as well as after Tab, because focus can move without a key
  the dialog saw.
- **A failed command's whole output goes in the detail pane**, with the first line in the footer.
  Git's errors (a rejected push, a conflicted merge) are paragraphs, and the footer is one line.
- **A conflicted file belongs to the working tree only.** Both sides of an unmerged path carry a
  letter, so read naively `UU` looks staged *and* modified and the file appears in both panels.
  `GitFileStatus.IsConflicted` is what keeps it in Unstaged, marked `!`; staging it is how git spells
  "resolved", and the dialog says so.
- **`P` publishes a branch that has no upstream** (`PushAsync(setUpstream: true)` → `push -u <remote>
  HEAD`, remote from `GetDefaultRemoteAsync`). A plain `git push` answers an unpublished branch with a
  paragraph of advice, which is never what the person who pressed the key wanted.
- **Remote-tracking branches are listed** (`GetBranchesAsync(includeRemotes: true)`) below the locals
  and drawn grey; checking one out is `checkout --track`, falling back to a plain checkout when the
  local branch already exists. The two ref spaces are read with two `for-each-ref` calls, because
  short names make a local branch called `origin/x` indistinguishable from a remote-tracking one.
- **`?` / F1 opens an in-dialog key reference**, and the panels are *hidden* while it is up rather
  than drawn over: `ControlRendererJob` renders controls from a `HashSet`, so nothing guarantees the
  parent's overlay lands after its children.
- **The inline input (commit / amend / new branch / stash message) tracks `_inputActive`, not the text
  box's `IsVisible`.** `Control.ShowAsync` makes every child visible on the way in, so a footer that
  reads `IsVisible` as "the input is up" never draws its key hints at all.
- Not built: in-pane conflict resolution (a 3-way view; resolving today means editing, staging and
  committing), tags, rebase, cherry-pick, and a multi-line commit body.

### The folder selector (`iTerminal.Core/Dialogs/FolderSelector*.cs`)
Ctrl+F1 opens `FolderSelectorDialog`: a vertical `Tab` whose seven pages are the seven ways of naming
a folder — Favourites, Drives, Recent, Places, Folder Tree, SSH, Path. Whichever page is up, the
keys are the same: the arrows move, **Enter goes**, Esc cancels, F1 explains.

**Recent** is most-recently-used first: `FolderHistoryService.Add` takes the folder out of the list if it is there, puts it on top, and drops the least recently used past the maximum. The list is saved in that order, so it survives a restart.

- **The left strip is clickable, and a click lands the cursor in the panel on the right.** Both
  halves matter: the strip is drawn by the `Tab` and owns no controls, so a click on a page name
  used to reach nothing (see the `Tab` mouse note under Key patterns), and a page cannot hold focus
  itself, so focusing it hands the cursor to the first control of the page — which is what makes a
  click on the strip a complete gesture rather than one that leaves the user reaching for Tab.
- **Choosing a folder ends the dialog**: every select command navigates and then calls
  `Command.CloseContainingDialog`, which walks up from the command's control to the enclosing
  `Dialog`. Both routes have to work — Enter on the page's list and the page's own *Select* button —
  and for a long time only the button did, because the Enter route depends on the page being handed
  the key at all (see the `Tab` note under Key patterns).
- **Esc closes it at any time**, from any page, whatever is focused — and where the page has a
  sub-state on screen, **one press per layer**: an Esc with a suggestion list up on the Path or SSH
  page dismisses the list and leaves the selector standing, the next closes the selector; the F1 help
  over the top is the same story through the topmost-modal rule. Nothing in the dialog implements
  either half. `Dialog.HandleKeyEventAsync` claims Esc *ahead of the children* rather than offering it
  to the focused control first, so no text box or list can swallow it — and because of that a
  suggestion list had no key that could dismiss it on its own, which is what
  `ITransientOverlayHost` exists for (see Key patterns). It is still pinned down by
  `FolderSelectorDialogTest` page by page, because it is the promise the dialog makes on its own
  border.
- **Selecting a page puts the cursor on that page's first control**, by every route — Ctrl+Up/Down,
  Ctrl+&lt;number&gt;, a click on the strip. A `TabPage` cannot hold focus itself, so
  `Tab.SwitchToPage` focusing the page hands the cursor to the first *focusable* control inside; that
  is the whole reason every page adds its list or its text box before its buttons. Ctrl+&lt;number&gt;
  goes through `SwitchToPage` even for the page already showing, which makes it the keyboard's way
  back into the content — read as "only when that page is not already focused", as it used to be, the
  key did nothing at all once the cursor had reached one of the page's buttons, and on the Path page
  (where Tab belongs to the completion) nothing brought it back.
- **The suggestion list is placed directly below the box it belongs to, which is why the text boxes
  are laid out one row high.** A `TextBox` draws exactly one line whatever its `ViewSize.Height`, and
  the list is a `Popup` with `PopupPlacement.Below` — so the Path and SSH boxes, laid out as tall as
  the whole page (a leftover from when the list was drawn *inside* the box's rectangle), put their
  suggestions at the bottom of the screen, a dozen rows from the text being completed. Leaving a page
  also takes the list down (`Tab.SwitchToPage`): the popup is attached to the *root*, so hiding the
  page it belongs to does not hide it.
- **Enter is claimed by `FolderSelectorTabPage`, the shared base, ahead of the page's children.**
  Each page's real work lives in a command attached to its *Select* button, but the control that
  holds focus is the list — and `ListBox<T>` binds Enter to its own `SelectListItemCommand`, which
  reports the key handled unconditionally. Routed the ordinary way, Enter ticked a row's selection
  flag and stopped: the select command was reachable only by tabbing onto the button. A focused
  `Button` still keeps Enter for itself, so "Add to favourites" under the cursor adds rather than
  navigates.
- **Every page lists its list first.** `SetFocus` lands on the first focusable control, and a page
  that opens with a button focused answers the arrow keys with nothing.
- **`SelectedDrive` follows the cursor, not `ListBox.SelectedItem`.** That property reports the row
  whose `IsSelected` flag is set — a flag only Enter ever sets — so read that way it is null the
  first time it matters.
- **The Path page is what makes every drive kind reachable.** It hands the typed text straight to
  `Url` and lets `ContentModelService.AddContentModel` decide what it is, so a scheme added to the
  app works there the day it is added, without touching the page. Tab completes, from the paths typed
  there before *and* the folders the panels have visited (`FolderPathCompletionModel` — the history
  is read live rather than copied into the settings key, which would both duplicate it on disk and
  freeze it). `FolderUrlValidationModel` accepts a known scheme on sight — a remote URL cannot be
  checked without doing the very work the navigation is about to do, and a validator that blocks on
  the network has no business running off a keystroke — and only probes the disk for a local path.
  Its `SupportedSchemes` is also what generates the on-screen scheme hint, so the two cannot drift.
- **The Places page carries the app's own drives** (`memdrive://`, `musiccatalog://`,
  `musicplaylist://`) beside the OS special folders. They are not on the Drives page because that one
  is fed by `DriveInfoModel`, which the status bar's drive selector also draws from; and they are
  listed *somewhere* rather than left to the Path page, because a URL scheme nobody has written down
  cannot be typed.
- **The dialog draws its own footer** by overriding `DrawChrome` — `PopUpWindow` always passes an
  empty footer — and shrinks it first, since `StringHelper.AlignCenter` drops an over-long footer
  whole. The pages are full-height lists with no room for a hint line of their own.
- Favourites are stored in the `Favourites` setting. Two things about that were wrong for a long
  time and are worth not reintroducing: the list was **cleared before the setting was read**, and
  `Clear` raises `CollectionChanged`, which writes the now-empty list straight back — so saved
  favourites were erased a line before they were read, every start-up; and the write joined the `Url`
  objects rather than their `FullUrl`, and `Url` does not override `ToString`.

### Synchronise folders (`SynchronizeFoldersDialog`, Edit menu, `iTerminal.Core/Dialogs/SynchronizeFoldersDialog.cs`)
The dialog holds three options and a list. The list is the plan (`FolderSynchronizer.Plan` over `FolderComparer`), built
on a background task by `p` and applied on the idle pass, so the keys never wait for the disk. Each option change drops the
list: a plan is only valid for the options it was built with. The run button is the same Enter that builds the plan, so a
plan that deletes is always seen before it runs. The test seam is `StartPreview`, `RunAsync` and the `ShownPlan` and `Status`
properties; the tests cover the option keys, the preview, the second Enter for deletions, and the two refusals (no task
services, no panel folder).

### Web Browser (`WebBrowserDialog`, `Model/Web/`, `Services/Web/`)
A full-screen, text-mode browser (Tools > Web Browser, Ctrl+S; Ctrl+T would open the Tools menu): tabs, address bar, back/forward, reload/stop, bookmarks, history, find in page, forms, cookies, page source (F9) and saving (F7). F1 lists the keys.

- **Dialog = screen, `WebBrowserModel` = behaviour.** The dialog only draws and routes keys; tabs, history, loading, forms and saving live in `WebBrowserModel`/`WebTab` so they are tested without a screen. Fetching and layout run on background tasks and land in the tab under `WebTab.Sync`; `Draw` only reads and starts a layout (`EnsureLayout`), so no key handler waits for the network. Only the newest load of a tab may land (`Generation`).
- **No JavaScript, no CSS, no images.** `HtmlParser` is a forgiving tree builder (implied end tags, raw-text elements, depth cap), `WebDocument` collects links, forms and fields, and `HtmlLayout` wraps it into styled `WebLine`s for a width: headings, lists, quotes, `pre`, definition lists, tables sized to their content, images as alt text. Block layout is memoised within one `Layout` call because tables measure their cells first.
- **Form controls reserve their width in the layout but are drawn live** from `WebField` state (a span with `Field` set), so typing never re-lays the page out. The state survives a re-layout and Back/Forward (each history entry keeps its document, page, scroll and focus; entries more than 8 away drop their data and are re-fetched).
- **`IWebFetcher` is the only network seam.** `WebFetcher` serves http(s) (one `HttpClient` and cookie jar for the app), `file:` (directories as a link list), `data:` and `about:`. Binary content is never saved unasked: the page says so and F7 writes it to `~/Downloads` without overwriting. The end-to-end suite replaces it with `SandboxServices.Web`.
- **Bookmarks and history** (`WebBrowserStore`) are JSON in the settings (`WebBrowser.*`); the home page and search URL (`{0}` = query) can be set there too.
- **F3 on a local `.html`, `.htm` or `.xhtml` file** opens the page here as a viewer (`ViewFileDialogCommand`, `HtmlFileTypes`). Enter does not: it opens the page in the external browser, through "Open with" or the system default, as for any other file. Each panel gets its own dialog (`Func<WebBrowserDialog>`), and the File menu and hot-key bar's F3 get the same one. The browser loads from disk, so a page on a remote drive or in an archive is not offered here and F3 shows it as text.

### Stock browser (`StockBrowserDialog`, Alt+F7)
A full-screen `SplitPaneDialog` (like the Git client) showing a live watchlist: symbol, name, price, change and change %, exchange, with the cursor stock's day and 52-week range, volume and last trade below the list. `a` adds, `d`/Delete removes (y/n in the footer), `r` refreshes now.

- **`IStockService` owns the list and the quotes; the dialog only views them.** The watchlist is JSON in the `Stocks.Watchlist` setting. Absent means "never touched" and gives the defaults (Apple, Microsoft, Tesla, Google, Nvidia, Intel, SpaceX = `SPCX`, Ethereum = `ETH-USD`, Bitcoin = `BTC-USD`; an existing saved list is left as it is); an emptied list is stored as `[]` and stays empty. Quotes live in memory.
- **Live means a refresh loop that exists only while someone is watching.** `Watch()` returns a handle; the first starts a `PeriodicTimer` loop (15 s), the last stops it. The dialog takes a handle in `OnInitializeAsync` and drops it in `Close`. A change raises `Changed` from the loop's thread and the dialog re-reads the rows in memory — no I/O on the key handler or in `Draw`.
- **Quotes and search come from `IStockQuoteProvider`** (`YahooStockQuoteProvider`: the public chart endpoint, one request per symbol, and the search endpoint). These are unofficial endpoints, so a symbol that cannot be reached is left out and the old quote stays; no quotes at all is shown in the footer. The former portfolio's Stooq provider answered 404 when this was written, which is why it is not reused. Symbols are Yahoo's: a bare ticker for the US, a suffix elsewhere (`VOD.L`, `SAP.DE`).
- **Adding searches every exchange.** `a` opens a query box; Enter searches by symbol or company name and lists each hit with its exchange, since one company trades on several; Enter on a hit adds it. A search with no hit offers the text as a symbol on the second Enter. `AddAsync` asks the provider for a quote first, so a typo is refused in words instead of sitting on the list.
- **The end-to-end suite replaces the provider** (`SandboxServices.StockQuotes`): no run reaches the network.

### Translator (`TranslatorDialog`, Ctrl+Shift+F4, `Services/Translation/`)
Tools > Translator: a text box, a From language (or *Detect language*), a To language and an engine; F5 translates, F7 detects, F6 swaps the languages and brings the translation back as the new text, F8 clears. The choices are kept in the `Translator.*` settings.

- **Two providers behind one `ITranslationService`.** `LibreTranslateProvider` is the API (any LibreTranslate-compatible server: `POST /translate`, `POST /detect`; `Translation:ApiUrl`, key from `LIBRETRANSLATE_API_KEY` or the password manager entry `translation:api-key`, read per request). `LlmTranslationProvider` uses the app's `IChatClient`, so whichever Ollama or OpenAI-compatible model the `AI` section names does the work; its prompt allows no commentary and the answer is stripped of `<think>` blocks. *Automatic* tries the API first when a URL is set and falls back to the model on a failure; a chosen engine is never fallen back from. `UnavailableReason` says why an engine cannot be used ("no translation API set up", "AI is off") without a request.
- **Detection is offline first.** `LanguageDetector` decides by script (Hangul, kana, Thai, Hebrew…), narrows Han/Cyrillic/Arabic by letters only some languages use, and scores Latin languages on common words plus giveaway letters (ß, ñ, ő, ğ…). A guess at 0.9 or more is taken as it is; a weaker one asks the engine. The info line under the pickers shows the guess as it is typed ("a guess") and the engine's answer once known.
- **Long texts are cut and joined back exactly.** `TranslationService.Chunk` cuts on line breaks, then sentence ends, then spaces, then hard at the limit (each provider has its own `MaxChunkLength`); the blank space around each piece is kept, and once one piece has reported the source language the rest are given it.
- **Nothing waits on the network.** A request runs on a background task and parks its outcome; `OnIdle` applies it on the UI thread, and only the newest request (by generation) may land. Closing cancels the one in flight.
- **Layout gotcha.** `TextViewerControl` (and the editor) centre themselves in their parent on every layout, so two side by side need the position-preserving `PaneEditor`/`PaneViewer` subclasses, and both must be `CanFocus` for Tab to reach them. Tab only moves among siblings, so they are direct children of the dialog, not of a wrapper pane.
- **The end-to-end suite replaces the providers** (`SandboxServices.Translation`): no run reaches the network or the model.

### Currency converter (`CurrencyConverterDialog`, Ctrl+Shift+F5, `Services/Currency/`)
Tools > Currency Converter: an amount, a From and a To currency; the result and the rate both ways are shown as you type. F5 downloads the rates again, F6 swaps the currencies. The choices are kept in the `CurrencyConverter.*` settings.

- **Rates are the ECB's daily reference file** (`EcbCurrencyRateProvider`: `eurofxref-daily.xml`, no key, about 30 currencies per euro, published on working days). `CurrencyRates` converts any pair through the euro. Behind `ICurrencyRateProvider`, so another source is one class.
- **Works offline.** `CurrencyService` keeps the last download in the `CurrencyConverter.Rates` setting and loads it at start-up; the dialog shows it at once and still downloads when the rates were not downloaded in this run (`IsStale`, six hours). A failed download never replaces the kept rates and the status says which ones are in use.
- **Nothing waits on the network.** Like the translator, the download runs on a background task, parks its outcome, and `OnIdle` applies it; only the newest download (by generation) may land.
- **The end-to-end suite replaces the provider** (`SandboxServices.CurrencyRates`: one euro is two dollars): no run reaches the network.

### BitTorrent manager (`TorrentManagerDialog`, Ctrl+Shift+F1, `Services/Torrents/`)
Tools > BitTorrent: every torrent with state, progress, size, speeds and peers, refreshed twice a second from `OnIdle`. A adds a magnet link, a `.torrent` path or the URL of one (and the folder to save to, default `~/Downloads` or the `BitTorrent.DownloadFolder` setting); Space pauses or resumes; D asks, then Y removes and keeps the files, W removes and deletes them; O opens the save folder in the active panel.

- **The engine is MonoTorrent**, wrapped by `TorrentService` (`ITorrentService`). It is created on first use, so a run that never opens the manager opens no sockets. Every action runs on a background task and reports on the status line; the dialog never waits on the engine.
- **Torrents survive a restart.** The list lives in the `BitTorrent.Items` setting (magnet links as they are; `.torrent` files copied to `torrents/` beside the database); the dialog calls `RestoreAsync` when it opens, and the engine's fast-resume files in `torrent-cache/` keep the pieces already downloaded. A torrent that cannot come back is logged and skipped.
- **Dispose stops the engine** (`TorrentService` is `IAsyncDisposable`, so the container does it at shutdown).
- **Not yet covered by the end-to-end sandbox:** the engine is only reached when a torrent is added, which no end-to-end test does; a test that adds one needs a `SandboxServices` replacement for `ITorrentService` first.

### Games (`GamesDialog`, Tools > Games, `Services/Games/`)
Tic-Tac-Toe, Chess, Go (9x9, 13x13, 19x19), Nine Men's Morris and Risk, against the computer or two people at one keyboard, and Snake, for one person. There is no hot key: every Ctrl+Shift+F-key is taken, so it opens from the Tools menu, the command palette or the AI `ui` tool ("games", "chess", "go", "snake", ...).

Arrows move the cursor, Enter or Space plays, PgUp/PgDn or 1-6 choose the game (the digit row follows the number of games), N starts again, U takes a move back (and the computer's answer with it), M switches between the computer and two players. A game may ask for more: Go has P (pass) and S (board size); chess asks for the promotion piece with Q, R, B or N; Risk has E (end this phase). Games that do not offer U or M leave those keys to the other controls (Snake offers neither; Risk offers no U).

- **A game is an `IGame`.** It owns its rules, its cursor and its drawing (`Render()` returns rows of `GameCell`: text, colours, a cursor flag); the dialog only forwards keys and paints. A game is one class plus an entry in the `GamesDialog` constructor, plus whichever optional `IGame` members it needs (`Tick`, `Pause`, `OffersComputerOpponent`, `OffersUndo`, `ActivateLabel`, `ActivateHint`). The dialog is a transient, but the games live as long as it does, so a closed dialog keeps its positions for the run.
- **Snake** (`SnakeGame`, Tools > Games, digit 5) is single-player on a 20 x 12 board. It starts stopped: the first arrow key or Enter sets it moving, arrows steer, and a turn straight back onto the body is ignored. Enter pauses and resumes it. It moves one cell per 120 ms; the dialog passes the time since its last idle pass to `Tick` from `OnIdle` (capped at one second), and the game counts that time, not the passes, so a busy screen gives fewer idle passes but not slower movement. A wall or the snake's own body ends the game. Leaving it for another game, or closing the dialog, pauses it and keeps its position.
- **Risk** (`RiskGame`, Tools > Games, digit 6) is a simplified Risk on a 6 x 4 grid of territories with four continents (NW and NE worth 2 bonus armies, SW 3, SE 4); there are no cards and no undo. Blue (the person) moves first, Red is the computer or the second person. Each turn is reinforce (`max(3, territories / 3)` plus the bonuses of the continents the side holds whole), attack (one battle per Enter: the attacker rolls up to three dice, the defender up to two, a tie goes to the defender; a conquest moves the attacker's dice count in), then fortify (one army to an adjacent own territory, which ends the turn). E ends the current phase. A side with no territories loses; reaching 300 turns is a draw. The computer plans one action per step on a background task against a snapshot of the position, and each step does nothing if the position has changed since (`Version`).
- **The computer never blocks a key handler.** `IGame.PlanComputerMove()` returns null unless it is the computer's turn, otherwise a function that runs on a background task against an immutable snapshot of the position and returns the action that plays the move. The dialog parks that action; `OnIdle` plays it on the UI thread. Every change bumps the game's `Version`, so a move chosen for a position that has since changed (undo, new game) does nothing, and closing or switching games cancels the search through its token.
- **Chess** (`ChessEngine.cs`) has the whole rulebook: castling through attacked squares, en passant, promotion choice, mate, stalemate, fifty moves, threefold repetition, dead positions. The move generator is checked against the published perft counts. The computer is an iterative-deepening alpha-beta search with a capture quiescence tail and a 1.5 s budget.
- **Go** has captures, no suicide, the simple ko rule, passing and area scoring with a 7.5 komi. After two passes the dead groups are marked by hand (Enter on a stone) and the score follows; Undo resumes play. The computer is a one-ply heuristic (captures, atari, liberties, eyes, own territory, line height); it passes when nothing is worth playing or when the person has passed and it is ahead.
- **Nine Men's Morris** has placing, sliding, flying at three men, mills that take a man outside a mill, and the 100-move draw. The computer is a time-budgeted alpha-beta search.
- **Colours.** Boards are drawn on fixed colours (a wood background for Go and Morris, a checker for chess) and not the theme's, because a "black stone" has to stay black on a light theme.
- **Labels are English, with one gap.** The footer's key words (`Arrows:move`, `PgUp/PgDn:game`, ...) and the help's key names are hard-coded English, as they were before; the Enter word in the footer and the Enter help (`ActivateLabel`, `ActivateHint`) go through `UiText` for Snake and Risk only (Risk's status and messages too; the other games keep their English defaults; Risk's rules and the help text for E stay English), and so does the "one player" mode label. Localizing the rest is a known gap.

### The contact manager (`ContactManagerDialog`, Ctrl+Shift+K, `Model/Contacts/`)
Tools > Contacts: name, email, phone, company and notes for the people the person keeps in touch with. N or Insert adds a contact and E, F2 or Enter changes the highlighted one, a field at a time (Enter or Tab for the next, Shift+Tab back, Enter on the last saves, Esc discards); the highlighted contact's fields are shown under the list. D or Delete asks y/n in the dialog. `/` filters the list as it is typed: every word must appear in some field, Enter keeps the filter, Esc clears it (and the next Esc closes the dialog).

- **Storage is the `Contacts` setting**, one JSON list read by `ContactStore` (same approach as the task list and the calendar): a table of its own would change the schema stamp and drop every cached table at start-up. A damaged value or a `null` entry is skipped, and the next save replaces it.
- **`Contact.Validate` is the one rule set:** a name is required, an email needs an `@` with text on both sides, a phone number needs a digit and only `+ - ( ) . /` and spaces. A refused save keeps the form open and moves the cursor to the field at fault.
- **The highlight follows the contact by id**, so it survives a re-sort or a filter; a contact saved under a filter that would hide it clears the filter.
- Also reachable from the `ui` AI tool as `contacts`.

### The encyclopedia (`EncyclopediaDialog`, Ctrl+Shift+W, `Services/Encyclopedia/`)
Tools > Encyclopedia: a term to type, a source to look it up in, the matching articles on the left and the one under the cursor on the right. Enter on the term searches; the cursor resting on a result reads that article's introduction; Enter on a result or F3 reads the whole article (F3 again goes back to the introduction); F2 opens the article in the browser. The source and the term are kept in the `Encyclopedia.Source` and `Encyclopedia.Query` settings; opening the dialog never searches by itself.

- **Sources are `EncyclopediaSources.All`:** Wikipedia in English, German, French, Spanish, Italian and Hungarian, the Simple English Wikipedia, and Wiktionary are `MediaWiki` sources read through the open Action API (`list=search` for the results, `prop=extracts` with `explaintext` for the text, `exintro` for the introduction only). The request carries a user agent that names the app, as the Wikimedia rules ask.
- **Britannica is a `LinkOut` source.** Its pages answer a program with a Cloudflare challenge ("Just a moment…"), and this app does not try to get around a bot check. Choosing it gives one result, the term itself, and F2 opens Britannica's own search in the browser through `IFileLauncher.OpenUrlAsync` (which refuses anything but `http` and `https`).
- **`EncyclopediaService` does what no provider can:** it answers link-out sources itself, trims the term, and remembers the last 40 articles read so going back to a result does not ask the network again. A failed read is never remembered.
- **Nothing waits on the network.** Searches and reads run on a background task and park an outcome that the next idle pass applies; only the newest request may land (checked under a lock in `Land`, so a cancelled request that finishes late cannot overwrite the newer outcome), and closing cancels the one in flight. The cursor over the results is polled from `OnIdle` and a read starts only once it has rested (`CursorDelayMilliseconds`), so holding Down does not fetch every row.
- **Tests never reach the network:** `TerminalApp` replaces `IEncyclopediaProvider` with `SandboxServices.Encyclopedia`.
- Also reachable from the `ui` AI tool as `encyclopedia`, `wikipedia` or `britannica`.

### The workspace manager (`iTerminal.Core/Dialogs/Workspaces/`, `Services/Workspaces/`, `Model/Workspaces/`)
Ctrl+Shift+F2 (Tools > Workspaces) opens `WorkspaceManagerDialog`: named screen layouts. A workspace is a
`WorkspaceLayout` — the left and right panel locations, each panel's sort order, what is under the panels
(`WorkspaceBar`: AI bar, command line, full-screen terminal) and which panel had the cursor — so one Enter
puts a whole working context back.

- **The list is in save order and the first nine are on the 1-9 keys.** A row's mark says where the screen
  stands against it: `●` it is what is on screen now, `○` it was the last one used but the screen has moved
  on (the detail pane says so and offers U). "Now" ignores the cursor's panel, which moves constantly.
- **Keys:** Enter / 1-9 switch (the dialog closes first, so the cursor can go back into a panel), S (or N)
  saves the screen under a name, U overwrites the selected one, R renames, D / Del deletes. U and D ask y/n in
  the status row; a save under a name that exists needs a second Enter. The prefilled name is selected, so
  typing replaces it and End/arrows keep it to edit.
- **`IWorkspaceService` owns the list** (JSON in the `Workspaces` setting, the active name in
  `ActiveWorkspace`, the way the remote connections are kept: no schema change). `IWorkspaceLayoutService`
  is the only code that knows panels and bars: `Capture()` reads them, `ApplyAsync()` writes the panel's
  sort setting, then the location (`SetUrlAsync(name, url)`), then the sort on the model, then the bar, then
  gives the focus back to the saved panel. `Content.CurrentBar` exposes the bar.
- **Restoring never fails the whole workspace.** A local folder that is gone is left as the panel has it and
  reported in the toast ("Left panel: C:\x no longer exists"); an unreachable remote reports the same.
  Nothing secret is stored — a remote panel is only its URL.

### The Bluetooth device manager (`iTerminal.Core/Dialogs/Bluetooth/`, `Services/Bluetooth/`, Ctrl+Shift+J)
Tools > Bluetooth devices lists the devices BlueZ knows: paired ones, ones seen recently, with their state (connected, paired, nearby). **S** scans for 8 seconds, **C** connects or disconnects the device under the cursor, **P** pairs, **Delete** asks y/n and then forgets it, **R** reads the list again.

- **The backend is `bluetoothctl` on Linux only.** `BluetoothService` runs it through `IExternalProcessRunner` (one short command per call, each with its own timeout), and `BluetoothctlParser` reads what it prints, so the formats are tested against recorded output. Windows reports `IsSupported == false` and the dialog says so; nothing is read there. A device that asks for a PIN cannot be paired here: no agent answers it.
- **`bluetoothctl` exits 0 after a refused connect.** `BluetoothctlParser.Failed` reads the text as well as the exit code (`Failed to`, `org.bluez.Error`, `not available`, `No default controller`), so the reason reaches the status line.
- **The highlight is the cursor.** `ListBox` keeps the highlight in `ScrollableModel.CursorPosition`; `ListItem.IsSelected` is the mark that Space toggles, and the dialog does not use it. A refresh rebuilds the rows, so the dialog remembers the highlighted address and `OnIdle` moves the cursor back to that device when the rows changed.
- **One operation at a time.** A key that starts a change is refused while another runs. The worker writes the result to `_statusText`; `OnIdle` puts it on the status line, so no worker touches the control tree. Each change reads the list again afterwards.
- **The dialog takes focus for its list when it shows.** A dialog with focus passes arrow keys to no child.
- **Tests never reach the machine's adapter.** `SandboxServices.Bluetooth` lists two made-up devices and answers every change with success; the unit tests use a recording fake.
- No setting is stored: the list is read each time the dialog opens.

### The remote connection manager (`iTerminal.Core/Dialogs/Remote/`, `Services/Remote/`, `Model/Remote/`)
Alt+F2 (Tools > Remote connections) opens `RemoteConnectionManagerDialog`: saved SSH, PowerShell remoting, **FTP and
WebDAV** targets. Enter on an SSH or PowerShell entry lends the whole terminal to the session through
`IInteractiveShellService` — the same hand-over as Ctrl+O — so it is a real `ssh` or `Enter-PSSession` with
its prompt, colours, full-screen programs and Ctrl+C; `exit` comes back to the manager. B opens an SSH
connection in the active panel as the existing SFTP drive. N / E / D add, edit and delete (D asks y/n). On the
form Space on the Type field cycles SSH, PowerShell, FTP, WebDAV (HTTP), WebDAV over HTTPS.

- **FTP and WebDAV entries are drives, not shells.** Enter and B both open `ftp://user@host[:port]/folder` or
  `dav[s]://…` in the active panel (`RemoteConnection.DriveUrl`; the port is left off when it is the default). They
  add a **Password** and a **Folder** field. `RemoteShellLaunch.TryCreate` refuses them ("opens as a panel").
- **Their password goes to the password manager, never into the entry's JSON.** A shell asks for its password on the
  terminal it is lent; a drive has none to ask on. `RemoteConnectionService.Save(connection, password)` writes the
  password as a `PasswordEntry` under the connection's id *and* a `NetworkConnectionProfile` with the same id
  (protocol, `RemoteConnection.DriveHost` = `host` or `host:port`, user, `HasSavedCredential`) — exactly what
  `ContentModelFtpDrive` / `ContentModelWebDavDrive` look up when a URL names a user, so the saved password is used
  with no other step. A blank password on a later edit keeps the one saved; no user name is an anonymous login (no
  profile is looked up, so none is kept). A locked password manager that cannot be opened refuses the save, and nothing
  is half-recorded. Delete, or changing the entry into SSH/PowerShell, removes the profile and the secret.
- **No shell password is ever stored.** The session gets the real terminal and asks there — password, key
  passphrase and host-key question alike. Only a file-drive entry has `HasSavedCredential`.
- **`RemoteShellLaunch` builds the program from the entry.** SSH is `ssh [-p port] [-i key] [user@]host`
  with no remote command, so the server starts the login shell. PowerShell is `pwsh|powershell.exe -NoLogo
  -NoProfile -Command "Enter-PSSession …"` — no `-NoExit`, so leaving the remote session ends the process.
  Transport `WinRm` uses `-ComputerName` (and `-Credential 'user'`, which makes PowerShell ask for the
  password) and is refused off Windows; transport `Ssh` uses `-HostName/-UserName/-KeyFilePath` and needs
  `pwsh` (7+) on PATH. `TryCreate` returns the reason instead of a launch, and the dialog shows it.
- **Host and user are validated before they reach any command line.** A host must start with a letter or
  digit (a leading dash would be an `ssh` option) and may hold only name characters or an IP address; a
  user name may not hold quotes, spaces or a leading dash. PowerShell values are also single-quoted with
  `'` doubled. `RemoteConnection.Validate` is the one gate, used by the form, the service and the launch.
- **The session runs on a thread of its own** (`PendingOperation`), never in the key handler; the result
  lands in the footer under the dialog's lock. The dialog stays open while the terminal is lent, so exit
  returns to the manager, and `MarkConnected` stamps "last connected".
- **The panel drive (B) has the drive's limits**: SSH only, port 22 only (the `ssh://` URL has no port), and
  credentials resolved by `ContentModelSshDrive` (a network profile or `~/.ssh/id_rsa`), not by this entry's
  key file. The dialog says so rather than opening a drive that would connect somewhere else.
- Entries are JSON in the `RemoteConnections` setting (`IRemoteConnectionService`). An FTP or WebDAV entry also
  writes a `NetworkConnectionProfile` (the network browser's store) so the drives find its login; the id is shared.
  A user name for those two may not hold `@ / \ :` — it is written into the drive's address.

### Mark by pattern and Format drive: failures stay in the dialog

- **Mark by pattern** (`MarkPatternDialog`, Num+ / Num-): a pattern that matches no row, or a panel that cannot be marked in, keeps the dialog open with the reason on its error line. It closes only when at least one row changed.
- **Format drive** (`FormatDriveDialog`): the drive letter must be typed back before anything is erased. A second Enter while a format is running says it is already running, rather than doing nothing.
- **Form dialogs** (`FormDialog`): the two buttons never share columns, even on a narrow dialog — Cancel is kept clear of the primary button.
- **Disk and network lists** (`DiskManagerDialog`, `NetworkManagerDialog`): `IsLoaded` means the first pass is over, whether or not every item answered. The job sets it in a `finally`; before, a failing pass left the list on "Reading…" for good.
- **Folder size counts** (`FileInformationRefreshTask`): a count that could not finish (a folder that is missing or unreadable) shows `?`, not the part that was counted.

- **Background work in a dialog says how it failed.** A `Task.Run` that sets a "Reading…", "Searching…", "Connecting to …" or "Adding…" message must clear it on every outcome, including an exception it did not expect. Catching only the network or I/O exceptions left those messages on screen for good (`DiskManagerDialog`, `SystemSettingsDialog`, `RemoteConnectionManagerDialog`, `MapDialog`, `StockBrowserDialog`).

### The XML viewer (`XmlViewerDialog`, F3 on an XML file, `iConsole/Xml/`)
F3 on an `.xml`, `.xaml`, `.csproj`, `.config`, `.resx`, `.svg` and the other XML types in `XmlFileTypes` opens the
document as a tree. `XmlTree` builds one `XmlNodeItem` per element, text run, comment and processing instruction, and
indexes each XML node to its item. An element whose only content is text shows that text on its own line.

- **Expand and collapse** are the `TreeView`'s own keys (Enter, Space, `+`, `-`, Left, Right); `*` expands every node. The
  title bar shows `+/-:Open/close` and `*:Expand all`; the title is shrunk around the file name so those hints always fit.
- **XPath** (`/` moves to the box, Enter runs it): `XmlXPathQuery` evaluates XPath 1.0 with the prefixes the root
  element declares. A node-set gives one result per node, shown as its absolute path (with `[n]` where siblings share
  a name) and its value; the first 500 are listed and the count says how many there were. Enter on a result opens its
  ancestors and moves the cursor to the node.
- **Validation** (F2): `XmlValidator` checks the document against the DTD its DOCTYPE names, or against the XSD files
  its `xsi:schemaLocation` and `xsi:noNamespaceSchemaLocation` attributes name. Schema and DTD files are read from the
  local disk next to the document only, never fetched. Each error or warning is listed with its line and column, and
  Enter on one opens the element it is about (`XmlTree.ItemAtLine`). A document that names neither is reported as a note.
  A missing schema is a warning from the framework, not an error.
- **Text** (F4) shows the file's text through the plain viewer's reader. A file that is not well-formed opens that way
  at once, with the parse error on the status line.

The document is parsed on a background task when the dialog opens, and a query runs on one too; both results are
applied on the idle pass, on the thread that owns the controls. The tree is the dialog's first focusable control, since
`Dialog` gives the keyboard to the first one that can take it.

### The JSON viewer (`JsonViewerDialog`, F3 on a `.json` file, `iConsole/Json/`)
F3 on a `.json` file opens the document as a tree. `JsonTree` builds one `JsonNodeItem` per member or item: a
property shows as `"key": value`, an array item as `[0]: value`, and a container shows how many members it has. Objects
and arrays with contents expand and collapse with the `TreeView`'s own keys; `*` expands every node.

- **Validation** (F2): `JsonValidator` parses the document with `System.Text.Json` and reports the first place it is not
  valid JSON, with its line and column. Comments and trailing commas are rejected, as RFC 8259 requires. A document is
  not checked against a JSON Schema; that needs a schema library, which this viewer does not use.
- **Text** (F4) shows the file's text through the plain viewer's reader. A file that is not valid JSON opens that way at
  once, with the error on the status line.

The document is parsed and validated on background tasks; the validation result is shown on the idle pass.
- **An abandoned error prompt**: when the operation that asked for a prompt is cancelled, `ErrorPromptService` marks the prompt abandoned, and the prompt removes itself on the UI thread at its next idle pass. Before, it stayed on screen, grabbing the keys.

### The solution viewer (`SolutionViewerDialog`, F3 on a `.sln` or `.slnx`, `Services/Dotnet/`)
F3 on a solution opens its projects, with build (`b`), clean (`c`), test (`t`) and run (`r`) on the checked projects, or
on the project under the cursor, or on the whole solution for build, clean and test. `x` cancels a run.

- **SDKs.** `DotnetSolutionService.ListSdks` reads `dotnet --list-sdks` off the UI thread. The newest stable SDK is the
  default (`DotnetSdk.Default`); a preview is chosen only when no stable one is installed. The header shows the selected
  SDK's version, and each command's first output line names it. `v` cycles through the installed SDKs; the next command
  uses the chosen one, and a running command keeps the SDK it started with. Each command runs the SDK's own `dotnet`
  host. A `global.json` in the solution folder can still pin a different SDK; the host applies it as usual.
- **Finishing a run.** A run ends when the `dotnet` process exits. Its output is then read for a short grace period
  (2 seconds), not until the pipe closes: MSBuild can leave a build node running that holds the pipe open, which used to
  leave the dialog busy after a command had finished.
- **Cancelling.** The run's task is cancelled before the process is killed. Killing raises the exit event at once, and
  that would otherwise report the run as finished with an exit code.

### The language selector (`LanguageDialog`, `Services/SettingsLanguageStore.cs`)
Ctrl+Shift+F3 (Tools > Language…) opens `LanguageDialog`: English, Deutsch, Français, Español, Italiano and Magyar, each by its own name so it
can be found whatever language is showing. ↑/↓ move, Enter or a click applies and closes. Applying calls `ILocalizer.SetLanguage`,
which saves the choice in the `Language` setting and raises `LanguageChanged`; the menu bar and the footer re-label on their next idle
pass (see Localization in [ui-framework.md](ui-framework.md)). The dialog's own title and key hints are looked up from the same resources.

### Performance benchmark (`PerformanceBenchmarkDialog`, Ctrl+Shift+B, `Services/Benchmark/`)
Tools > Performance benchmark measures this machine in eleven probes, in five groups (CPU, Memory, Disk, Network, Latency), and scores each one. A group's score is the geometric mean of its probes, and the overall score is the geometric mean of the groups, so one fast group cannot hide a slow one. Enter runs it; Esc cancels a run that is going, and closes the dialog otherwise.

- **Read only, enforced by the probes.** The disk probes open the largest file of at least 8 MB in the program folder (or the .NET runtime folder) with `FileAccess.Read` and `FileShare.Read`, and never create, rename or change anything. `BenchmarkFiles` and the disk tests are the place to keep it that way: `TheDiskProbesReadAFileAndLeaveTheFolderExactlyAsTheyFoundIt` snapshots names, sizes and write times before and after. Reading still updates a file's access time where the OS does so, which is not a write by the program.
- **Scores are relative to a reference machine.** `BenchmarkScore.ForMeasurement` gives 1000 at the probe's reference value and scales in proportion (or inversely for a latency). The reference values are the constants in each probe, chosen by hand, not measured on a reference box, so the absolute numbers are a yardstick for comparing runs on this app, not a figure to publish.
- **Network probes never reach beyond what they name.** Loopback throughput stays on this machine (a listener on 127.0.0.1). Internet latency and download use `speed.cloudflare.com` (a 10 MB download). A probe that cannot reach its host is recorded as unavailable and left out of the scores; the tests point it at an unreachable loopback port.
- **A probe that fails never stops the run.** `PerformanceBenchmarkService` catches a probe's exception and records it as unavailable; cancellation is the only thing that ends a run early, and it returns the scores so far with `IsComplete` false.
- **The dialog never waits on a measurement.** The run is a background task that reports each probe's start and result through an `IProgress`; the result lands in a snapshot under a lock, and `Draw` reads only that snapshot. Closing the dialog cancels the run.
- **The end-to-end suite replaces the probes** (`SandboxServices.BenchmarkProbes`), with the same ids and categories and fixed figures, so no run measures the machine it runs on.

### CSV viewer (`CsvViewerDialog`, F3 on a `.csv` or `.tsv` file, `iConsole/Csv/`, `iConsole/Controls/CsvGrid.cs`)
F3 opens a delimited file as a grid: the header on top, the rows below, and the columns scroll sideways to keep the cursor cell on screen. The query line above the grid takes a small SQL-like query, `SELECT name, amount WHERE amount > 10 AND city = 'Pécs' ORDER BY amount DESC LIMIT 20`, and the status line says how many rows matched. F4 switches to the file's text, as in the JSON and XML viewers.

- **The parser and the query are in `iConsole`, with no UI.** `CsvDocument.Parse` reads RFC 4180 style quoting (a quoted field may hold the delimiter, a line break or a doubled quote). Without a delimiter it picks the candidate (`,` `;` tab `|`) that splits the first record into the most fields. A `.tsv` file is always read with tabs. A short row reads as empty in the missing columns; a blank line is skipped; a file past `MaxRows` keeps its first rows and says so.
- **`CsvQuery` is a parser that builds a predicate.** `SELECT *` or a column list, then `WHERE` (the word is optional when the query starts with a condition), then `ORDER BY` and `LIMIT`. `AND` binds tighter than `OR`; there are no parentheses. Numbers compare as numbers when both sides are numbers, otherwise text ignoring case. Errors name the column that does not exist and list the columns; a malformed query says what it expected.
- **A query runs on a background task.** `StartQuery` starts it and returns; `OnIdle` puts the result in the grid when it lands, and only the newest query may land. A query that fails to parse keeps the rows already on screen and shows its error on the status line.
- **The file is read with `TextEncodings.Decode`** (a byte order mark, else UTF-8 when valid, else the system's ANSI code page), never as lossy UTF-8, so a Windows-1250 file shows its accents. **F8 / Shift+F8** read the grid again under the next / previous code page of `TextEncodings.Available`; the title names the current one. `Encoding` changes at once (quick presses keep advancing), the re-read runs on a background task (`SwitchTask`) and lands on the next `OnIdle`; it drops a query that was running and skips the byte order mark. F8 is always handled, so it never falls through to Delete in the panels or Shift+F8 to the settings, but it does nothing while the file is unread or the F4 text view shows. **The F4 text view always picks its own code page** (`FileToTextConverterService`, as in the F3 viewer) and does not follow the grid's.
- **`CsvGrid` takes the keyboard only while enabled and focused**, lists its keys for the help screen, takes the focus on a click, and repaints its whole rectangle. Column widths come from the header and the first 500 rows, within 3 to 40 cells.
- **Tests:** `CsvDocumentTest` (parsing), `CsvQueryTest` (the language), `CsvViewerDialogTest` (loading, queries, the status line, `.tsv`, F8 code pages), `EditorViewerAndPermissionsTest` (F8 and Shift+F8 in the real app) and `ViewFileDialogCommandTest` (F3 routes `.csv` and `.tsv` here).
- Not built: sorting by clicking a header, saving a query's result, and parentheses in the condition.

### SQLite viewer (`SqliteViewerDialog`, F3 on a local `.sqlite`, `.sqlite3`, `.db` or `.db3` file, `iConsole/Sqlite/`, `iTerminal.Core/Services/Sqlite/`)
F3 opens a database as a result grid. The query line takes one SQL statement, run read-only; Enter runs it, and F4 switches to the result as aligned plain text. The default query lists the schema (`sqlite_master`), so a database opens on its tables, views and indexes.

- **The UI has no SQLite dependency.** `iConsole` holds the contract (`ISqliteDatabaseReader`, `SqlTable`, `SqlQueryException`), the file-type check and the text formatter. `iTerminal.Core` holds `SqliteDatabaseReader`, which uses `Microsoft.Data.Sqlite` (pinned in `iTerminal.Core.csproj`).
- **Three write guards, all of which must be bypassed to write.** (1) `SqliteStatementChecker` runs first: it masks comments and the inside of quotes and brackets, so a `;` or `/*` in a string is never code. Only one statement is allowed, and its first keyword must be `SELECT`, `WITH`, `EXPLAIN` (checked recursively) or `PRAGMA`. A `PRAGMA` must be one of a fixed list of read-only names and may not contain `=`. (2) The connection opens `Mode=ReadOnly`. (3) `PRAGMA query_only = ON` is set after open. `WITH ... DELETE` is valid SQLite and passes the checker; the read-only connection is what stops it, and a test checks the file's bytes are unchanged afterwards. Extension loading stays off.
- **Limits.** A query may run for 10 seconds (`CommandTimeout`), which also applies to a legitimate large query; a longer one shows the error on the status line. Results keep at most 10,000 rows and say so. A `NULL` cell reads `NULL`, a blob reads `<blob N bytes>`.
- **Cancellation is a native interrupt.** `SqliteCommand.Cancel` does not stop a running step, so cancelling the token calls `sqlite3_interrupt` on the connection's handle. A newer query or closing the dialog cancels the running one; a cancelled or stale result is dropped on the idle pass and writes nothing to the status line.
- **Files.** Only a local file opens here: a remote `.db` falls through to the generic text viewer, as before. The first 16 bytes must read `SQLite format 3` followed by a NUL, so a non-SQLite `.db` file, and an empty one, is refused with "Not an SQLite database" before SQLite opens it. A WAL database opens when its `-wal` file is present; if SQLite cannot open it, the error appears on the status line.
- **Tests:** `SqlTableTextTest` and `SqliteViewerDialogTest` (iConsole), `SqliteStatementCheckerTest`, `SqliteDatabaseReaderTest` (temp files only, including the WITH-DELETE byte check, a runaway query cancelled promptly, and WAL), and `ViewFileDialogCommandTest` (F3 routes a local database here and leaves a remote one alone).
- Not built: writing to the database, a table browser, export, and multi-statement scripts.

### Printing and the print queue (`PrintDialog`, Ctrl+P, `Services/Printing/`)
Ctrl+P (also File > Print…, the right-click menu on a local file and the command palette) prints the file under the cursor and manages the queue of a printer, in one dialog. Left/Right choose the printer (remembered in the `Print.Printer` setting), `+`/`-` the copies, Enter prints, P holds or resumes the job under the cursor, Del asks (y/n) and then cancels it, F5 reads the queue again.

- **The key is Ctrl+P, not Ctrl+Shift+P**: Windows Terminal (and VS Code, WezTerm) keep Ctrl+Shift+P for their own command palette, so the key would never arrive. Ctrl+P has no global binding in the app; Alt+P/Alt+N are the command history, and the two Ctrl+P uses in `PropertiesDialog` and `FindFilesDialog` are local to those dialogs.
- **`PrintDialogCommand` always runs** (`CanExecute` is true): with no local file under the cursor (the `..` row, a folder, an archive row, another drive) it opens as the queue manager alone, and Enter says there is nothing to print. The file is read the way `SignatureDialogCommand` does (`GetFocusedModel() ?? GetLastContentModel()`, `PathType.File` and `ContainerType.FileSystem`) and handed over in `OnBeforeShow`. The right-click menu, which lists every command that can run, has a small branch for Print (`HasPrintableFile`) so it is offered on a local file only.
- **`IPrintService` runs the platform's tools through `IExternalProcessRunner`**: Windows PowerShell 5.1 (`powershell`, not `pwsh`: PrintManagement is native there) with `Get-CimInstance Win32_Printer`, `Get-PrintJob`, `Suspend-/Resume-/Remove-PrintJob`; CUPS elsewhere (`lpstat -p -d`, `lpstat -l -o`, `lp -d -n -- file`, `lp -i printer-id -H hold|resume`, `cancel`). A job is named by printer **and** id: Windows ids count per printer.
- **Nothing typed becomes code.** CUPS values are separate arguments (`--` before the file). On PowerShell a printer name and a path are embedded only through `SignatureService.Literal`; the shell's `PrintTo` verb needs a second layer of double quotes, so a printer name with a double quote prints text files only. A name starting with `-`, any control character, a relative or missing path and a job id below zero are refused before a tool starts. Copies are kept between 1 and 99.
- **Windows prints text itself and hands everything else on**: a file with a text extension (`PrintService.TextExtensions`) goes through `Out-Printer`, any other through the `PrintTo` verb, which needs a program associated with that type and says so when there is none. The extension rule is Windows only; `lp` takes text, PDF and images as they are. A `PrintTo` job reaches the queue only once that program has spooled it, which the periodic read catches.
- **A tool that does not answer is a sentence.** `ExternalProcessRunner` already stops a tool after 30 s (exit code -1); the service cancels after 15 s, tighter, and turns both into a failure sentence (`PrintResult`, `PrinterListing.Failure`, `QueueListing.Failure`). The caller's own cancellation (the dialog closing) is thrown, not reported.
- **The queue is polled, never waited on.** Every call runs on a background task. While the dialog is open the queue is re-read every 5 s on Windows (each read starts PowerShell) and every 2 s elsewhere, a round is skipped while a call is still out, and it is read again right after any action. A generation counter drops a reply for another printer or an older request; closing the dialog cancels what is in flight.
- **The end-to-end suite registers `SandboxServices.Printing`**, in-memory printers and jobs that record every call: no test may print on a printer of the machine it runs on. `PrintServiceTest` runs both platforms' command lines against `FakeProcessRunner`; `PrintDialogTest`, `PrintDialogCommandTest`, `FileContextMenuTest`, `PrintLanguageTest` and `PrintingTest` cover the rest. The Windows and CUPS command lines have not been run against a real printer.
- Not built: adding printers, printer properties and printer-level pause, print-to-PDF, headers and pagination for text, remote files.

### The virtual machine manager (`VirtualMachineManagerDialog`, Shift+Alt+F9, Tools > Virtual Machine Manager, `Services/VirtualMachines/`)
- **Linux only, through libvirt.** `LibvirtVirtualMachineService` runs `virsh -c qemu:///system` (a CLI, like the docker drive, so nothing native is packaged); console opens `virt-viewer` detached. The command's `CanExecute` and the Tools menu entry are both false off Linux. Hyper-V is not written.
- **Keys:** S starts; Enter starts a shut-off machine and does nothing otherwise; D shuts down, R restarts, Shift+X powers off and V saves state, all with a y/n in the status line; P pauses or resumes; C opens the console; F5 lists again. The dialog has no text box, so letters are commands and `ListBox.EnableTypeAhead` stays off. Enter is claimed by the dialog ahead of the list.
- **The rule that makes the list safe:** `virsh` runs on a background task and stores its answer under a lock; `OnIdle` applies it on the UI thread. The list is read again every 5 s while the dialog is open.
- **Failures are sentences.** A `virsh` run that outlives the runner's 30 s limit says the hypervisor may still be saving and to press F5; a refused connection names the `libvirt` group.
- **The end-to-end suite registers `SandboxServices.VirtCli`**, a recorded `virsh` that never reaches a hypervisor, and a `virt-viewer` stand-in that records the console instead of opening one.
- Not built: Hyper-V, snapshots, create and delete, CPU and memory columns.
