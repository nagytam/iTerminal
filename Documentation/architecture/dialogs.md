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
- **Git sync (Ctrl+Shift+G) opens `GitSyncDialog`** (`Dialogs/Git/GitSyncDialog.cs`), not a one-key pull and push. It lists the current branch and its upstream (ahead/behind), the conflicted files, and the local and remote branches, and offers the pull mode (`GitPullMode`: rebase, merge, fast-forward only) and a push-after-pull tick. Enter runs the sync: pull, then push if ticked; a failed step stops there and the conflicts it left are listed; the failure's guidance goes on a second status line, and a sync or branch switch asked for while one runs says so rather than queueing. The list shows its visible range at the right end of the rule when it scrolls. The **Clean up merged** button (Alt+C) lists the local branches whose tip is already in the main branch (not the main branch itself, nor the checked-out one) and asks y/n in the status line before deleting; the run re-reads the merged list and the checked-out branch, then deletes with `-D` (each tip is verified contained in main, so nothing is lost), and reports what it could not delete. `GitSyncCommand` only opens the dialog, and is offered only inside a working tree.
- Not built: in-pane conflict resolution (a 3-way view; resolving today means editing, staging and
  committing), tags, rebase, cherry-pick, and a multi-line commit body.

### The folder selector (`iTerminal.Core/Dialogs/FolderSelector*.cs`)
Ctrl+F1 opens `FolderSelectorDialog`: a vertical `Tab` whose nine pages are the nine ways of naming
a folder — Favourites, Drives, Recent, Places, Folder Tree, SSH, Path, SMB, FTP/SFTP. Whichever page is up, the
keys are the same: the arrows move, **Enter goes**, Esc cancels, F1 explains.

**SMB (`FolderSelectorSmbTabPage`, Ctrl+7)** is the network neighbourhood for SMB. The list starts with the hosts that answered on port 445 in a scan (`INetworkDiscoveryService`) the saved SMB connections and the hosts with a saved login; Enter on a host lists its shares, Enter on a share goes to `smb://[user@]host/share/` and closes the selector, and Backspace returns to the hosts. The line above the status shows the SMB version (2 or 3) the host was listed with, read from the negotiated dialect (`SmbFileSystemService.SmbVersionOf`). SMBLibrary's `SMB2Client` cannot speak SMB 1, so SMB 1-only hosts are not listed.

- **Browsing is anonymous; a login is typed on the page.** A host that lists no shares anonymously is logged in to with L (the Log in button): a username and then a password are asked for, the host's shares are listed with them, and the login is kept in the password safe under `SmbLoginKey` (`smb-login:host\user`). Each scan lists the shares of every saved login again, and opening a share as that user finds the password there (`ContentModelSmbDrive.Connect`). A refused login saves nothing. A line above the status names who is logged in to the host under the cursor (or that it is browsed anonymously), and O logs out after a y: the logins to that host and their saved passwords are removed, and a panel already connected as that user stays connected. With the password safe locked the saved logins are not listed, and a new login is listed but reported as not kept.
- **A saved connection still works the same way.** The Network Browser (Ctrl+N) saves a connection with its password; its login replaces the discovered share of the same name, so the address carries the user name.
- **The scan never runs on the render thread.** It runs on a background task, is stopped after `ScanLimit` (30 s), and parks its hosts under a lock; `OnIdle` applies them to the list. The row rules live in `SmbBrowserRows`, which the tests pin without a screen.

**FTP/SFTP (`FolderSelectorRemoteFilesTabPage`, Ctrl+8)** lists the saved FTP and SFTP connections as folders to go to. The rows are the saved network profiles of those two protocols (`NetworkProtocol.Ftp` and `NetworkProtocol.Ssh`), because those are what the drives log in with: the FTP password, the SFTP key. An FTP profile opens at the folder of the Remote connections entry with the same id, if there is one, and otherwise at the root; an SFTP profile opens at `ssh://user@host:/`. Enter goes to the row under the cursor and closes the selector. Nothing is scanned on the page. **N** (or the New server… button) opens `FolderSelectorAddServerDialog` over the selector: a type (FTP or SFTP (SSH)), host, port (FTP only), user, password, folder (FTP only) and an optional name. Enter (Add and open) saves the entry through `IRemoteConnectionService.Save`, so it is listed on the page and in Remote connections from then on, points the panel at it, and closes the selector with the form; Esc closes only the form. An SFTP server is always on port 22 and opens at its root, the same panel the SSH page's Connect opens. The password goes to the password safe (a blank one is anonymous for FTP, and key or agent for SFTP); a locked password manager keeps the form open with the reason. The rest of the connections are managed in Remote connections (Alt+F2), and an SSH login for a shell is kept by the Network Browser (Ctrl+N). Saving an SSH entry writes its SSH profile (host, user, key), so it is listed here too; an SSH entry saved before that gets its profile the first time the page opens. An SSH entry with a password keeps it for the SFTP drive; saving the entry again without one keeps the saved password. An SSH entry on a non-default port gets no profile, since the SSH drive cannot connect on a port. The row rules live in `RemoteFilesBrowserRows`.

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
  Ctrl+Left/Right (the dialog maps them to Ctrl+Up/Down before the tab sees them), Ctrl+&lt;number&gt;,
  a click on the strip. A `TabPage` cannot hold focus itself, so
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
- **Every page answers to Alt+&lt;number&gt; as well as Ctrl+&lt;number&gt;.** A legacy terminal sends no byte for Ctrl+0, Ctrl+1 or Ctrl+9 (it sends the digit) and sends Ctrl+2 to Ctrl+8 as control codes the decoder does not name, so the Ctrl form only arrives with the Kitty keyboard protocol or on the Windows console. Alt+digit is ESC plus the digit everywhere, and `FolderSelectorDialog` rewrites it to the Ctrl form before the children see it, so the Path page's text box does not type the digit. Tests for the Ctrl form inject synthetic events, which is why they never caught this.
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
  app works there the day it is added, without touching the page. Tab completes, live from the folders on disk (`DirectoryCompletionModel`, local paths only; see file-operations.md) and from the paths typed
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

### AI action approval (`AiActionApprovalDialog`, `iTerminal.Core/AI/`)
AI file-writing tools for folder creation, copy, move, rename, delete, archive creation/extraction and folder sync
preview when `confirmed=false`. With `confirmed=true` they open an app-owned approval dialog; folder creation, transfers,
rename and archive operations show their source/destination and relevant options, delete shows every target, and sync
shows the full plan in a scrollable viewer. Y approves, while N, Esc or cancellation refuses the operation. The
model's tool argument only requests the dialog and cannot authorize the action. Approval prompts are serialized so
concurrent tool calls cannot stack modal dialogs.

### Web Browser (`WebBrowserDialog`, `Model/Web/`, `Services/Web/`)
A full-screen, text-mode browser (Tools > Network > Web Browser, Ctrl+S; Ctrl+T would open the Tools menu): tabs, address bar, back/forward, reload/stop, bookmarks, history, find in page, forms, cookies, page source (F9) and saving (F7). F1 lists the keys.

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
- **The watchlist and the search hits are `Table<T>`s** (`Table<StockRow>`, `Table<StockSearchHit>`). `WatchlistColumns(width)` and `HitColumns(width)` set the columns for the line width; the company name takes what the other columns leave, and on a narrow line the exchange, then the change in percent, then the change go first. A rise is green and a fall red through `RowForeground`, on rows not under the cursor. The tables redraw on `CollectionChanged`; the stock dialog's own heading is the table's header row.
- **The end-to-end suite replaces the provider** (`SandboxServices.StockQuotes`): no run reaches the network.

### Password manager (`PasswordManagerDialog`, Ctrl+Shift+X)
The saved passwords, listed by category, title and username; the password is hidden in the detail line until V shows it.

- **The list is a `Table<PasswordEntry>`, and its cursor is the only cursor.** The dialog's keys (Up/Down, Page, Home, End, Enter, N, D, V, Esc) move the table's cursor through `MoveCursor`; a click or the wheel moves it through the table itself, and the dialog reads `CursorRow` back. The table is hidden outside list mode (`SetMode` and `SyncListVisibility`), and it is not focused, so the dialog's key handling sees every key first.
- **No secret in a column.** The columns are category, title and username. The password is drawn only in the detail line, and a new highlight hides it again.
- **Reloads keep the entry.** `Reload(keepId)` highlights the saved or edited entry when it is still listed, and otherwise keeps the cursor row, clamped.
- **Tests:** `PasswordManagerDialogTest` draws the table and its headings, checks that no password is drawn, and checks that a click followed by Enter edits the clicked entry.

### Translator (`TranslatorDialog`, Ctrl+Shift+F4, `Services/Translation/`)
Tools > Translator: a text box, a From language (or *Detect language*), a To language and an engine; F5 translates, F7 detects, F6 swaps the languages and brings the translation back as the new text, F8 clears. The choices are kept in the `Translator.*` settings.

- **Two providers behind one `ITranslationService`.** `LibreTranslateProvider` is the API (any LibreTranslate-compatible server: `POST /translate`, `POST /detect`; `Translation:ApiUrl`, key from `LIBRETRANSLATE_API_KEY` or the password manager entry `translation:api-key`, read per request). `LlmTranslationProvider` uses the app's `IChatClient`, so whichever Ollama or OpenAI-compatible model the `AI` section names does the work; its prompt allows no commentary and the answer is stripped of `<think>` blocks. *Automatic* tries the API first when a URL is set and falls back to the model on a failure; a chosen engine is never fallen back from. `UnavailableReason` says why an engine cannot be used ("no translation API set up", "AI is off") without a request.
- **Detection is offline first.** `LanguageDetector` decides by script (Hangul, kana, Thai, Hebrew…), narrows Han/Cyrillic/Arabic by letters only some languages use, and scores Latin languages on common words plus giveaway letters (ß, ñ, ő, ğ…). A guess at 0.9 or more is taken as it is; a weaker one asks the engine. The info line under the pickers shows the guess as it is typed ("a guess") and the engine's answer once known.
- **Long texts are cut and joined back exactly.** `TranslationService.Chunk` cuts on line breaks, then sentence ends, then spaces, then hard at the limit (each provider has its own `MaxChunkLength`); the blank space around each piece is kept, and once one piece has reported the source language the rest are given it.
- **Nothing waits on the network.** A request runs on a background task and parks its outcome; `OnIdle` applies it on the UI thread, and only the newest request (by generation) may land. Closing cancels the one in flight.
- **Layout gotcha.** `TextViewerControl` (and the editor) centre themselves in their parent on every layout, so two side by side need the position-preserving `PaneEditor`/`PaneViewer` subclasses, and both must be `CanFocus` for Tab to reach them. Tab only moves among siblings, so they are direct children of the dialog, not of a wrapper pane.
- **The end-to-end suite replaces the providers** (`SandboxServices.Translation`): no run reaches the network or the model.

### Market terminal (`MarketTerminalDialog`, Alt+Shift+F7, `Dialogs/Terminal/`)
A full-screen, command-line market view in the style of a trading terminal, on the same stock services as the stock browser (Tools > Office). The top line is a command line: type a symbol (`VOD.L`) or a company name and press Enter, and the security screen below the monitor shows its name, exchange and currency, the last price with its change, the previous close and volume, the day's range, where the price sits in the year's range, and the last trade. Enter on an empty line shows the monitor's cursor row; Up/Down move through the monitor; F5 refreshes both; F6 puts the security on the watchlist or takes it off. A ticker tape across the second line lists every watched stock.

- **A watched security stays live.** `OnIdle` copies the watchlist's quote onto the security screen whenever the refresh loop has changed it, and the heading says "watched". A security that is not on the list keeps the quote it was looked up with until F5.
- **The chart is the security's daily closes.** Between the monitor and the security screen, a `LineChart` plot (braille, `LineChart.Render`) of the closes over the period, with the highest and lowest close on its right; green when the period ends above where it began. F8 cycles the period (1M, 3M, 1Y, 5Y; `StockHistoryRange`, Yahoo's `range=` parameter). `IStockQuoteProvider.GetHistoryAsync` fetches it on a background task and the dialog parks the answer like a lookup, so only the newest request lands. A new stock starts with an empty chart; F5 reloads the chart and keeps the one on screen until the newer answer arrives. The end-to-end suite answers with a synthetic 30-day series (`SandboxServices.StockQuotes`).
- **F6 adds through the service.** Adding asks the source first (`AddAsync`, on a background task, sharing the lookup's busy flag); removing is in memory. The service's own messages ("already on the list", "no quote") are shown as they are.

- **A lookup never joins the watchlist.** `IStockService.QuoteAsync` asks the source for one symbol and keeps nothing. A name that has no quote of its own is looked up through its best search hit, and the screen says so when nothing is found.
- **Lookups are parked, like the currency converter's.** The lookup runs on a background task and parks its result; `OnIdle` applies it on the UI thread. Only the newest lookup may land, so an older one that finishes late is dropped. Tests have no idle job, so they pump `OnIdle` themselves.
- **Reuses the stock browser's table.** The monitor is a `Table<StockRow>` built from `StockBrowserDialog.WatchlistColumns`, the same columns and the same rise/fall colours as the browser, so the two always agree on prices and colours.
- **The end-to-end suite needs no sandbox replacement:** the lookups go through `IStockService`, which the suite already replaces with `SandboxServices.StockQuotes`.

### Currency converter (`CurrencyConverterDialog`, Ctrl+Shift+F5, `Services/Currency/`)
Tools > Currency Converter: an amount, a From and a To currency; the result and the rate both ways are shown as you type. F5 downloads the rates again, F6 swaps the currencies. The choices are kept in the `CurrencyConverter.*` settings.

- **Rates are the ECB's daily reference file** (`EcbCurrencyRateProvider`: `eurofxref-daily.xml`, no key, about 30 currencies per euro, published on working days). `CurrencyRates` converts any pair through the euro. Behind `ICurrencyRateProvider`, so another source is one class.
- **Works offline.** `CurrencyService` keeps the last download in the `CurrencyConverter.Rates` setting and loads it at start-up; the dialog shows it at once and still downloads when the rates were not downloaded in this run (`IsStale`, six hours). A failed download never replaces the kept rates and the status says which ones are in use.
- **Nothing waits on the network.** Like the translator, the download runs on a background task, parks its outcome, and `OnIdle` applies it; only the newest download (by generation) may land.
- **The end-to-end suite replaces the provider** (`SandboxServices.CurrencyRates`: one euro is two dollars): no run reaches the network.

### Weather (`WeatherDialog`, Tools > Weather, `Services/Weather/`)
Tools > Weather: the saved places on the left and, on the right, the place under the cursor: its conditions now, then the next 24 hours (a temperature line with the rain under it) or the next 7 days (a table, `v` switches). `a` finds a place by name and Enter on a hit saves it; `d`/Delete removes one (y/n in the footer); `h` makes the cursor's place the home place; F5 fetches the forecast again. There is no hot key: every Ctrl+Shift+F-key is taken.

- **`IForecastService` owns the places and the forecasts; the dialog only views them.** The places are JSON in `Weather.Places`, the home place's key in `Weather.Home`, and the last forecast of each place in `Weather.Forecast`, so the dialog opens on the last forecast offline. A failed download never replaces it. The dialog opens on the home place, or on the first saved place when there is no home; with no saved place it opens on the search.
- **The forecast and the place search are Open-Meteo's** (`OpenMeteoForecastProvider`: the forecast endpoint and the geocoding endpoint, no key). The forecast uses Celsius and the place's own time zone. `WmoWeather` turns the WMO weather codes into one of fifteen descriptions and a glyph; an unknown code reads as "Unknown". The menu-bar indicator is still the IP-based weather and does not follow the home place.
- **Nothing refreshes while the dialog is closed.** The cursor's place is fetched from `OnIdle` when its forecast is older than 15 minutes; a failed attempt waits the same 15 minutes before the next, so a dead network is not asked on every pass. Only the newest search may land, and a place removed during a download is not saved again.
- **Letters are shortcuts only in the places list.** In the query box the keys type; in the hit list `a` returns to the query box. Esc steps back one pane at a time: hits to the query box, the query box to the places, and only then closes the dialog. With no saved place the query box is the only pane, so Esc closes it. Tab moves between the query box and the hit list, since those are the two panes up together.
- **The end-to-end suite replaces the provider** (`SandboxServices.WeatherForecasts`: Budapest, with a fixed day): no run reaches Open-Meteo. The menu-bar weather is already left out of the suite.

### BitTorrent manager (`TorrentManagerDialog`, Ctrl+Shift+F1, `Services/Torrents/`)
Tools > BitTorrent: every torrent with state, progress, size, speeds and peers, refreshed twice a second from `OnIdle`. A adds a magnet link, a `.torrent` path or the URL of one (and the folder to save to, default `~/Downloads` or the `BitTorrent.DownloadFolder` setting); Space pauses or resumes; D asks, then Y removes and keeps the files, W removes and deletes them; O opens the save folder in the active panel.

- **The engine is MonoTorrent**, wrapped by `TorrentService` (`ITorrentService`). It is created on first use, so a run that never opens the manager opens no sockets. Every action runs on a background task and reports on the status line; the dialog never waits on the engine.
- **Torrents survive a restart.** The list lives in the `BitTorrent.Items` setting (magnet links as they are; `.torrent` files copied to `torrents/` beside the database); the dialog calls `RestoreAsync` when it opens, and the engine's fast-resume files in `torrent-cache/` keep the pieces already downloaded. A torrent that cannot come back is logged and skipped.
- **Dispose stops the engine** (`TorrentService` is `IAsyncDisposable`, so the container does it at shutdown).
- **Not yet covered by the end-to-end sandbox:** the engine is only reached when a torrent is added, which no end-to-end test does; a test that adds one needs a `SandboxServices` replacement for `ITorrentService` first.

### Games (`GamesDialog`, Tools menu, `iTerminal.Core.Games/` for the engines, `iTerminal.Core/Dialogs/Games/` for the dialogs)
Every game opens in a dialog of its own (`TicTacToeDialog`, `ChessDialog`, `GoDialog`, `NineMensMorrisDialog`, `SnakeDialog`, `TetrisDialog`, `JassDialog`, `RiskDialog`, each with its own command in the Tools > Games submenu). They derive from the abstract `GamesDialog`, which hosts the game list when given several games and hides the list, PgUp/PgDn and the digit keys when given one. Every game plays against the computer by default, and M switches to a second person at the same keyboard (Tic-Tac-Toe, Chess, Go, Nine Men's Morris, Swiss Jass) or to one player (Snake, Tetris). Against the computer, Snake runs a rival snake for the same food, and Tetris runs a second board on the same clock; the first to crash or top out loses. Tetris: arrows slide and soft-drop, Up or X turns, Z turns back, D drops at once, Enter pauses. Swiss Jass Schieber: against the computer the person is Player 1 and the other three seats are the computer's (Player 3 is the partner); otherwise four people share the keyboard. There is no hot key: every Ctrl+Shift+F-key is taken, so each opens from the Tools > Games submenu, the command palette or the AI `ui` tool ("tictactoe", "chess", "go", "morris", "snake", "tetris", "jass"/"schieber").

Arrows move the cursor, Enter or Space plays, PgUp/PgDn or the game-list digit range chooses the game, N starts again (after a y/n when a game is in progress: Snake and Tetris are paused while the question is up, Esc or n keeps the game), U takes a move back (and the computer's answer with it), M switches between the computer and two players. A game may ask for more: Go has P (pass) and S (board size); chess asks for the promotion piece with Q, R, B or N. Games that do not offer U or M (Snake, Jass) leave those keys to the other controls.

- **A game is an `IGame`.** It owns its rules, its cursor and its drawing (`Render()` returns rows of `GameCell`: text, colours, a cursor flag); the dialog only forwards keys and paints. A game is one class plus an entry in the `GamesDialog` constructor, plus whichever optional `IGame` members it needs (`Tick`, `Pause`, `OffersComputerOpponent`, `OffersUndo`, `ActivateLabel`, `ActivateHint`). The dialog is a transient, but the games live as long as it does, so a closed dialog keeps its positions for the run.
- **Snake** (`SnakeGame`, Tools > Games, digit 5) is single-player on a 20 x 12 board. It starts stopped: the first arrow key or Enter sets it moving, arrows steer, and a turn straight back onto the body is ignored. Enter starts it, pauses it, resumes it, and after a crash starts a new game; the footer's Enter label names which of these it does. It moves one cell per 120 ms; the dialog passes the time since its last idle pass to `Tick` from `OnIdle` (capped at one second), and the game counts that time, not the passes, so a busy screen gives fewer idle passes but not slower movement. A wall or the snake's own body ends the game. Leaving it for another game, or closing the dialog, pauses it and keeps its position.
- **The computer never blocks a key handler.** `IGame.PlanComputerMove()` returns null unless it is the computer's turn, otherwise a function that runs on a background task against an immutable snapshot of the position and returns the action that plays the move. The dialog parks that action; `OnIdle` plays it on the UI thread. Every change bumps the game's `Version`, so a move chosen for a position that has since changed (undo, new game) does nothing, and closing or switching games cancels the search through its token.
- **Chess** (`ChessEngine.cs`) has the whole rulebook: castling through attacked squares, en passant, promotion choice, mate, stalemate, fifty moves, threefold repetition, dead positions. The move generator is checked against the published perft counts. The computer is an iterative-deepening alpha-beta search with a capture quiescence tail and a 1.5 s budget.
- **Go** has captures, no suicide, the simple ko rule, passing and area scoring with a 7.5 komi. After two passes the dead groups are marked by hand (Enter on a stone) and the score follows; Undo resumes play. The computer is a one-ply heuristic (captures, atari, liberties, eyes, own territory, line height); it passes when nothing is worth playing or when the person has passed and it is ahead.
- **Nine Men's Morris** has placing, sliding, flying at three men, mills that take a man outside a mill, and the 100-move draw. The computer is a time-budgeted alpha-beta search.
- **Swiss Jass** (`JassGame`, Schieber) uses a 36-card deck and four seats, either the computer's three or hot-seat people (`JassComputer` picks trump, pushes and cards for the computer seats on a background task, with a short pause so the trick can be followed), with teams 1+3 and 2+4. The forehand chooses trump or pushes once to the partner; 3–3–3 dealing starts at the forehand and the dealer rotates each hand. Enter reveals the current player's hand before it is shown, C/D/H/S chooses trump, P pushes, and 1–9 plays a card. Players must follow suit, trump when void, and overtrump when able (including when their partner is currently winning); this is the enforced local-play rule. Trump Jack/Nine are worth 20/14, the normal Jass card values apply, and the last trick adds five points for 157 per hand. The partnership that first reaches 1,000 wins.
- **Board layout.** The status shows up to three lines (a longer one ends on an ellipsis). The board takes the rows under the status, leaving two for the rules; a taller board scrolls to keep the cursor in view and marks the edges it is cut at with ↑ ↓ ← →.
- **Narrow screens.** The game list needs 64 columns. Below that the list is hidden and a line reads "Game 2 of 6" with the PgUp/PgDn hint, so the other games are still discoverable.
- **Colours.** Boards are drawn on fixed colours (a wood background for Go and Morris, a checker for chess) and not the theme's, because a "black stone" has to stay black on a light theme.
- **Risk** (`RiskGame`, `RiskDialog`, Tools > Games) is a simplified Risk on a 6 x 4 grid of territories with four continents, for one person against the computer (Red) or a second person at the same keyboard (Blue moves first). Each turn has three phases: reinforce, attack and fortify, and E ends the current phase. There are no cards and no undo. A side that loses every territory loses; a game that reaches the turn limit is a draw.
- **Labels are localized.** The footer words, the help key names and the game text go through `UiText` / `GameText` with an English default, and each key is in all six resource files. The mode label and the "computer is thinking" note are keys too.

### The contact manager (`ContactManagerDialog`, Ctrl+Shift+K, `Model/Contacts/`)
Tools > Contacts: name, email, phone, company and notes for the people the person keeps in touch with. N or Insert adds a contact and E, F2 or Enter changes the highlighted one, a field at a time (Enter or Tab for the next, Shift+Tab back, Enter on the last saves, Esc discards); the highlighted contact's fields are shown under the list. D or Delete asks y/n in the dialog. `/` filters the list as it is typed: every word must appear in some field, Enter keeps the filter, Esc clears it (and the next Esc closes the dialog).

- **Storage is the `Contacts` setting**, one JSON list read by `ContactStore` (same approach as the task list and the calendar): a table of its own would change the schema stamp and drop every cached table at start-up. A damaged value or a `null` entry is skipped, and the next save replaces it.
- **`Contact.Validate` is the one rule set:** a name is required, an email needs an `@` with text on both sides, a phone number needs a digit and only `+ - ( ) . /` and spaces. A refused save keeps the form open and moves the cursor to the field at fault.
- **The highlight follows the contact by id**, so it survives a re-sort or a filter; a contact saved under a filter that would hide it clears the filter.
- Also reachable from the `ui` AI tool as `contacts`.

### Online radio (`OnlineRadioDialog`, Ctrl+Shift+Y, `Model/Radio/`, `Dialogs/Radio/`)
Tools > Online radio: a list of internet radio stations, played through the shared `IMusicPlayerService`. Enter plays the highlighted station, Space pauses or resumes it when it is the one on air (otherwise starts it), S stops. F marks or unmarks a favourite; favourites are listed first, and V shows the favourites alone. R shows the last five stations started, most recent first (a station is recorded when Enter starts it, not on resume). N or Insert adds a station, E or F2 changes it (name, genre, stream address), D or Delete removes it after a y/n. `/` filters by name, genre or address, as the contact manager does. The list marks the station on air with a green `▶` and its name in green, and a favourite with `★`. Each station shows a genre icon (`Model/Radio/RadioGenre.cs`: the first known word of the genre wins, so `business, news` is `$` and `music, oldies` is `↺`; no genre is `•`). The line under the list shows the state (`●` on air, `‖` paused) with the station's icon, name and elapsed time, or the player's error. The details line shows the icon with the genre, and `★ Favourite` in yellow when the highlighted station is one.

- **A station plays as the only track of the queue.** `ReplacePlaylist` replaces the music queue, so the music panels' queue is lost when a station starts. A live stream has no end, so "next" would only switch stations; keeping the queue would have meant either a second player or a surprising jump.
- **A stream address is a track path.** `AudioDecoderFactory.IsStreamUrl` tells an http(s) address from a file. Such an address is decoded by `FfmpegAudioDecoder(path, isStream: true)`, which skips the duration probe (a live stream would wait out ffprobe's timeout), cannot seek, and reconnects when the server drops the connection. ffmpeg is therefore required for radio; without it the status line says so, and nothing is probed in the dialog itself.
- **Storage is the `RadioStations` setting**, one JSON list read by `RadioStationStore` (the same approach as the contacts), so no schema change. The recently played list is the `RadioRecent` setting, a JSON list of at most five station ids. A station is never listed twice: a stream address already in the list is refused on add and on change.
- **The list is preseeded from `DefaultRadioStations`** (popular stations from the US, UK, Germany, Switzerland and Hungary) when the setting has never been saved. Loading does not save, as with the calendar; the first change writes the whole list, and a list the person empties stays empty. A damaged value is not replaced by the seeds, so it is not hidden by them.
- **The list order is favourites first, then by name**, and the highlight follows the station by id, so marking a favourite keeps the cursor on it when the row moves.
- The footer's playback line is repainted from `OnIdle` by comparing a signature of the track, state, elapsed second and status message; the dialog does not subscribe to the player, whose events come from the pump thread.
- Also reachable from the `ui` AI tool as `radio`.

### The calendar (`CalendarDialog`, Ctrl+D, `Model/Calendar/`, `Jobs/CalendarIdentityJob.cs`)
- A month grid with the selected day's meetings under it. N or Enter types a meeting as "HH:mm [minutes] title"; E or F2 changes the highlighted one; D or Delete removes it after a y/n question.
- **The time zone is shown with a map.** A line under the week gives the system zone's name and its current UTC offset. In the margin left of the grid, `SchematicWorldMap.RenderBraille` draws the world in Braille patterns (2 × 4 dots per character), with the zone's meridian as a line of dots and a `●` marker on it at the 45th parallel: the system gives the zone's offset, not a latitude, so only the longitude is real. The zone is read through `LocalTimeZone` (a test sets a fixed one). The labels reuse the F1 popup's `Dialog.DateTimeTimeZone.*` strings, so no new translations were needed.
- **Every meeting has an identity** (`Meeting.Uid`, a GUID). `MeetingStore.Add` gives it and returns the stored copy; `Replace` keeps it through an edit, so a changed meeting stays the same event; `Remove` matches on it alone, so a meeting that was never stored removes nothing. The dialog moves its cursor by the stored copy.
- **Storage is the `CalendarMeetings` setting**, one JSON list, as for contacts and tasks (see the contact manager above and the Settings rule in CLAUDE.md).
- **Identities for a list saved before they existed are given at load, in memory, and written off the render thread.** Loading never saves, since a save is a blocking settings write and the calendar is read while drawing. `CalendarIdentityJob` writes the pending list every few seconds on its own thread; a failed write is logged and tried again on the next tick, so an identity is not lost to one failure. Until that write succeeds, a restart before it would give those meetings new identities.

### The watch (`WatchDialog`, Ctrl+Shift+T, Tools > Watch, `Model/Watch/`, `Services/Watch/`, `Jobs/WatchAlarmJob.cs`)
A horizontal `Tab` with four pages: the **World clock** (the computer's zone first, then the zones the person adds by their system id; the zone under the cursor is shown large, digital or analog, and V switches), the **Alarm** (daily, on or off with Space, the next one counted above the list), the **Stopwatch** (Space, L for laps, R when stopped) and the **Timers** (N starts one at once, Space pauses, R restarts).

- **Alarms and zones are kept in the settings table** (`WatchAlarms`, `WatchWorldClocks`), one JSON list each, read on first use, like `MeetingStore`. Timers and the stopwatch live for the session only.
- **Ringing is not the dialog's job.** `WatchAlarmJob` checks once a second and posts a notification, so an alarm rings and a timer is announced whether or not the dialog is open. An alarm is a notification that stays until dismissed. The day it rang is recorded before the save (`WatchStore.MarkRung`), so a failed save cannot ring it twice in the same minute. An alarm does not ring while iTerminal is closed.
- **Every page writes its own rows.** A page is focusable through a surface that fills it, and the digits and the clock face are drawn by the page (`Digits.Render`, `AnalogClock.Render`), not by child controls: children share their parent's layer and the renderer does not promise their draw order, so a surface drawn after them would paint over the face.
- **Time is passed in, never read.** `WatchRun` takes `now` on every call, and the dialog, the stopwatch and the job all read `IDateTimeProviderService`, so the tests drive a fake clock.
- **Esc and the arrows belong to the open entry.** While an entry or a y/n question is open, Esc cancels it and Left/Right do not switch tabs under the text being typed (`WatchDialog.HandleKeyEventAsync`). A single entry error takes the whole message line, since a prompt plus the typed text would cut it off at the dialog's width.
- The analog face is the `AnalogClock` control in `iConsole/Controls/`; its `Render` is static, so a page can place it without a child control.

### The task list (`ToDoListDialog`, Ctrl+Y, `Model/ToDo/`, `Services/ToDo/`, `Jobs/ToDoReminderJob.cs`)
Add (N or Enter), edit (E or F2), delete (D or Delete, asks first), mark done (Space), priority (P), hide done (H) and
**remind me (R)**. The entry line reads `!Subject @yyyy-MM-dd ~time`: `~15:30` is the next time that clock reading comes round,
and `~2026-11-01T09:00` is that exact moment, which must still be ahead. R opens the same line for the highlighted task
alone, pre-filled with its reminder; an empty entry clears it.

- **The reminder is one field on the task (`RemindAt`, local time), and the job is what gives it.** `ToDoReminderJob` checks
  every five seconds on its own thread, so a reminder comes whether or not the list is open, and one that fell due while the
  app was closed is given on the first check after start-up. The check goes through `ToDoStore.TakeDueReminders`, which clears
  each reminder and saves in the same step under the store's lock, so a reminder is never given twice.
- **A reminder stays on screen until it is dismissed** (`Timeout.InfiniteTimeSpan`), because the person is often away from the
  keyboard when it falls due.
- **Completing a task clears its reminder**, and the job skips done tasks, so a reopened task does not fire for a time that has
  long passed.
- **The edit line carries the reminder**, so editing a task and saving it round-trips the time. Saving an edit without a `~`
  token clears the reminder, the same way it clears a due date that is left out.
- **Times in the past are refused on the entry line** (`~2026-…` in the past), while a bare `~HH:mm` that has already passed
  means tomorrow at that time.
- Like the other lists, the tasks live in the `ToDoTasks` setting and not in a table, for the schema-stamp reason in
  `ContactStore`.

### The environment variables editor (`EnvironmentVariablesDialog`, Ctrl+Shift+H, `Services/SystemSettings/`)
Tools > Environment variables: the variables of one scope, name and value, with the highlighted one shown in full under the list. **S** cycles the scopes the system offers: *Session*, *User* and *Machine*. N or Insert adds (name, then value; Enter on the value saves), E, F2 or Enter changes the value (the name is fixed), D or Delete asks y/n, `/` filters by name or value, R reads again. A one-line note under the scope strip says how far the scope reaches.

- **It is a second front for the machine-settings layer, not a second implementation.** *User* and *Machine* go through `ISystemSettingsService` (the same `IEnvironmentVariableStore`s and validation the System Settings dialog uses, Ctrl+Shift+F10), so elevation checks and the registry store that keeps `Path` expandable are shared. What this dialog adds is the *Session* scope and a dialog of its own.
- **Where a scope lives depends on the system.** Windows: `HKCU\Environment` / the Session Manager key (`RegistryEnvironmentStore`). Linux: the user's `environment.d` and `/etc/environment` (`EnvironmentFileStore`); systemd reads these for user sessions at login, so a shell opened from a terminal emulator may not see them. macOS: a block between `# >>> iTerminal environment >>>` markers in `~/.zprofile` (`ProfileEnvironmentStore`, `export NAME='value'`, single-quoted), read by new login shells (what Terminal.app opens); there is no Machine scope. The store touches only its own block and keeps the rest of the file, its line endings and its final newline. A begin marker with no end is left as text and a fresh block follows it.
- **Session is the running process.** `ISessionEnvironment` (`SessionEnvironment`) sets and removes with `Environment.SetEnvironmentVariable(name, value)`; the `EnvironmentVariableTarget` overloads must not be used for User or Machine (they rewrite the registry value as plain text on Windows and are ignored on Unix). A session change reaches iTerminal and what it starts (command line, Ctrl+O shells, tools) and nothing else.
- **Nothing waits on the registry or a file.** Reads and changes run on a task and park an outcome (`Outcome`) that `OnIdle` applies; only the newest read may land (`_generation`) and one change runs at a time. A change reads the list again when it finishes. The form checks name and value with `SystemSettingsValidator` first, so a refusal keeps it open.
- **Ctrl+Shift+H, not Ctrl+Shift+E:** Ctrl+Shift+E is the file encryption (and the text editor's line endings) and Ctrl+Shift+X the password manager, so the editor has its own key; the menu and the command palette show it.
- **The end-to-end suite replaces both services** (`SandboxServices.SystemSettings` and `SandboxServices.SessionEnvironment`): no run touches the registry, a profile or the test host's own environment.
- Also reachable from the `ui` AI tool as `environment`, `env` or `variables`.

### The encyclopedia (`EncyclopediaDialog`, Ctrl+Shift+W, `Services/Encyclopedia/`)
Tools > Encyclopedia: a term to type, a source to look it up in, the matching articles on the left and the one under the cursor on the right. Enter on the term searches; the cursor resting on a result reads that article's introduction; Enter on a result or F3 reads the whole article (F3 again goes back to the introduction); F2 opens the article in the browser. The source and the term are kept in the `Encyclopedia.Source` and `Encyclopedia.Query` settings; opening the dialog never searches by itself.

- **Sources are `EncyclopediaSources.All`:** Wikipedia in English, German, French, Spanish, Italian and Hungarian, the Simple English Wikipedia, and Wiktionary are `MediaWiki` sources read through the open Action API (`list=search` for the results, `prop=extracts` with `explaintext` for the text, `exintro` for the introduction only). The request carries a user agent that names the app, as the Wikimedia rules ask.
- **Britannica is a `LinkOut` source.** Its pages answer a program with a Cloudflare challenge ("Just a moment…"), and this app does not try to get around a bot check. Choosing it gives one result, the term itself, and F2 opens Britannica's own search in the browser through `IFileLauncher.OpenUrlAsync` (which refuses anything but `http` and `https`).
- **`EncyclopediaService` does what no provider can:** it answers link-out sources itself, trims the term, and remembers the last 40 articles read so going back to a result does not ask the network again. A failed read is never remembered.
- **Nothing waits on the network.** Searches and reads run on a background task and park an outcome that the next idle pass applies; only the newest request may land (checked under a lock in `Land`, so a cancelled request that finishes late cannot overwrite the newer outcome), and closing cancels the one in flight. The cursor over the results is polled from `OnIdle` and a read starts only once it has rested (`CursorDelayMilliseconds`), so holding Down does not fetch every row.
- **Tests never reach the network:** `TerminalApp` replaces `IEncyclopediaProvider` with `SandboxServices.Encyclopedia`.
- Also reachable from the `ui` AI tool as `encyclopedia`, `wikipedia` or `britannica`.

### The YouTube client (`YouTubeDialog`, Tools > YouTube, `Services/YouTube/`)
Tools > YouTube: a search to type, the videos it finds on the left and the details of the one under the cursor on the right (channel, duration, views, upload date, link). Enter on the search runs it; Enter on a video, or F3, plays it in the F3 video viewer (`VideoFileViewerDialog`, with the video's title as its name); F2 opens the video's page in the browser. There is no global hot key.

- **`YtDlpYouTubeService` runs yt-dlp, and no API key is needed.** A search is `yt-dlp --flat-playlist --dump-json -- ytsearchN:<term>` (one JSON object per line; a line without an id or a title is skipped). Playback is `yt-dlp -g -f "b[height<=720]/b"` for the address of one combined stream, which `FfmpegVideoDecoder` then opens like a file. The term is passed as one argument after `--`, and a video id must match the eleven-character shape before it reaches the command line.
- **yt-dlp is the `yt-dlp` runtime dependency** (`YtDlpRuntimeDependency`): installed by the system's package manager like ffmpeg, and compatible within the year of its version (`2025.09.26` is the year 2025), because YouTube changes often enough that a dated yt-dlp stops working. A missing yt-dlp is reported by the dialog, not by a crash, with a pointer to Tools > Runtime Dependencies.
- **Nothing waits on the network in the dialog.** A search and a stream lookup run on background tasks; the outcome is parked and applied by the next idle pass, and only the newest request may land. The details pane follows the cursor from the list itself, so it needs no fetch.
- **Tests never run yt-dlp:** `TerminalApp` replaces `IYouTubeService` with `SandboxServices.YouTube` (one fixed video, and a stream address that does not resolve).

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
Tools > Bluetooth devices lists the devices the host knows: paired ones and, on Linux, ones seen recently, with their state (connected, paired, nearby). On Linux **S** scans for 8 seconds, **C** connects or disconnects the device under the cursor, **P** pairs, **Delete** asks y/n and then forgets it, **R** reads the list again.

- **The Linux backend is `bluetoothctl`.** `BluetoothService` runs it through `IExternalProcessRunner` (one short command per call, each with its own timeout), and `BluetoothctlParser` reads what it prints, so the formats are tested against recorded output. A device that asks for a PIN cannot be paired here: no agent answers it.
- **Windows lists only.** `BluetoothService` runs one `powershell -NoProfile -NonInteractive -Command` (`WindowsBluetoothParser.ListScript`): `Get-PnpDevice -Class Bluetooth`, keeping the device nodes (`BTHENUM\DEV_<12 hex>` classic, `BTHLE\DEV_…` low energy) and dropping the per-service nodes, with the `System.Devices.Connected` property per device, as compact JSON. `WindowsBluetoothParser` reads it: every device is *paired* (Windows keeps a node only for a paired device), a node that is not present is dropped, a device with both a classic and an LE node is listed once, and a device that does not report the connected property reads as not connected (best effort). The address comes from the instance ID. Expect a few seconds for a listing (PowerShell start-up plus one property query per device); it runs on the dialog's background operation. A scan, connect, pair or forget would need the WinRT `Windows.Devices.Bluetooth` APIs and a Windows-specific target framework, so they are not offered yet.
- **`IBluetoothService.CanChange`** (default: `IsSupported`; false for the Windows backend) tells the dialog whether the changing keys work. Without it the dialog offers only **R**, answers **S**, **C**, **P** and **Delete** with a status line at once (Delete does not ask y/n), and the empty list does not advertise scanning. `BluetoothService` takes its backend (`BluetoothBackend`) from the operating system; tests pass it explicitly, so the Windows path runs on any machine. With neither backend `IsSupported` is false and the dialog says so; nothing is read.
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
form is a `FormPanel` of `TextBox`, `PasswordBox` and two `SegmentedControl`s, so the cursor, selection, copy and
paste work in every field; Left/Right on the Type field choose SSH, PowerShell, FTP, WebDAV (HTTP) or WebDAV over
HTTPS, and a field the type does not use is dimmed and skipped by Tab. Enter or Ctrl+S saves; Esc goes back at once,
or asks y/n first when anything in the form changed. The form is attached to the dialog only while an entry is edited.

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
  `pwsh` (7+) on PATH, or the PowerShell 7 that Tools > Runtime Dependencies downloads (its folder is put on the PATH
  at start-up). `TryCreate` returns the reason instead of a launch, and the dialog shows it.
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
- **Disk usage bar** (`DiskManagerDialog`): the job reads `Drive.UsedFraction` on the drive's own task, and the row and the "Used" line only draw it. A row is replaced when the fraction changes, not only when its text does, since the bar moves with free space the rounded sizes do not show.
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
- **Validation** (F2): `XmlValidator` (behind `IXmlValidator`, in `iConsole.Formats/Xml/`) checks the document against the DTD its DOCTYPE names, or against the XSD files
  its `xsi:schemaLocation` and `xsi:noNamespaceSchemaLocation` attributes name. Schema and DTD files are read from the
  local disk next to the document only, never fetched. Each error or warning is listed with its line and column, and
  Enter on one opens the element it is about (`XmlTree.ItemAtLine`). A document that names neither is reported as a note.
  A missing schema is a warning from the framework, not an error.
- **Text** (F4) shows the file's text through the plain viewer's reader. A file that is not well-formed opens that way
  at once, with the parse error on the status line.

The document is parsed on a background task when the dialog opens, and a query runs on one too; both results are
applied on the idle pass, on the thread that owns the controls. The tree is the dialog's first focusable control, since
`Dialog` gives the keyboard to the first one that can take it.

### The JSON viewer (`JsonViewerDialog`, F3 on a `.json`, `.ipynb` or `.geojson` file, `iConsole/Json/`)
F3 on a `.json` file (and a Jupyter notebook or GeoJSON document, which are JSON under their own names; `JsonFileTypes.Extensions`) opens the document as a tree. JSON Lines (`.jsonl`) is one document per line and stays in the text viewer. `JsonTree` builds one `JsonNodeItem` per member or item: a
property shows as `"key": value`, an array item as `[0]: value`, and a container shows how many members it has. Objects
and arrays with contents expand and collapse with the `TreeView`'s own keys; `*` expands every node.

- **Validation** (F2): `JsonValidator` (behind `IJsonValidator`, in `iConsole.Formats/Json/`) parses the document with `System.Text.Json` and reports the first place it is not
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
- **SBOM.** `s` writes a CycloneDX 1.6 software bill of materials of the whole solution beside the `.slnx`, as
  `<Name>.cdx.xml` (the default) or `<Name>.cdx.json`; `f` switches the format. The packages come from each project's
  `obj/project.assets.json` (`GenerateSbom`), so the projects must have been built or restored: a solution without
  such files writes nothing and says so. The file is replaced on each write. No process runs, and the write is
  off the UI thread.
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

### Network tools (`NetworkToolsDialog`, Tools > Network tools, `Services/NetworkTools/`)
Tools > Network tools opens a tool (a combo box), a target (a text box) and the output below them. The tools are ping, traceroute, DNS lookup, reverse DNS, whois, a TCP port check, the headers of a web address and a subnet scan. Enter runs the tool on the target; Esc stops a run that is going and closes the dialog otherwise.

- **The subnet scan pings each usable address of an IPv4 subnet** (`192.168.1.0/24`, prefix 22 to 32, at most 1024 addresses, so a /22 is the largest). Probes run 32 at a time with the ping timeout (`PingTimeoutMilliseconds`, 2000 ms), so a /22 where every address times out takes about 64 s. A probe that fails is no reply for that host. A live host's line waits for its reverse lookup, which may take up to 2 s (`ReverseLookupTimeoutMilliseconds`) and runs under a gate of 8 lookups: a lookup that is not done in time, or finds no free slot, leaves the name off. A slot stays taken until its DNS call returns, because that call cannot be cancelled, so a run of hung lookups can use up the gate for the rest of the session. The gate is an instance field of the singleton service, so every scan in the app shares it. Lines are written under one lock, progress is counted as probes finish, and the summary comes after the last line. Esc returns at once and writes nothing after it returns.

- **The tools are in .NET, not in external programs.** `NetworkToolService` pings with `System.Net.NetworkInformation.Ping`, resolves with `Dns`, asks whois over TCP port 43 (following the `refer:` lines, at most two referrals) and checks ports with `TcpClient`, so the same output appears on Windows and Linux. An ICMP ping that the OS refuses to send reports the refusal as its output.
- **A target is checked before anything is sent.** `NetworkToolTargets` rejects a host with a scheme, a path or a space, a port outside 1 to 65535, an IPv6 address without brackets, and a web address that is not http or https. The dialog shows that reason on the status line; nothing is contacted.
- **Nothing waits on the network.** A run is a background task. Each line goes into a queue, and `OnIdle` moves the queued lines into the output (capped at 2000 lines) and applies the outcome. The status line says "Running…" until then; a failure says why on the status line.
- **The end-to-end suite replaces the service** (`SandboxServices.NetworkToolRunner`, fixed answers), so no run pings or queries anything.
- **Tests:** `NetworkToolTargetsTest` (target rules, whois referrals), `NetworkToolServiceTest` (port check against a loopback listener and a closed port, refused targets) and `NetworkToolsDialogTest` (the tool and target reach the service, output lands on the idle pass, a refused target and Esc).

### File transfer (`FileTransferDialog`, Tools > File transfer, `Services/Sharing/`, `iConsole/Qr/`)
Tools > File transfer shares the entries the focused panel would act on (marked entries, or the focused one) over plain HTTP on the LAN, and shows the address as a QR code and as text. A phone downloads the files from its browser. The share runs only while the dialog is open: Esc or closing it stops the server.

- **The share is a background service, not a dialog job.** `FileShareService` binds the first free port from 8080 to 8089 on all interfaces, serves each connection in plain text (no TLS, so no certificate warning on the phone, and the traffic is readable on the LAN), and reads requests with `iConsole/Net/HttpRequestReader.cs` (8 KB head, 10 s timeouts). Start, encoding and the bind run on a background task; the dialog reads an immutable `FileShareState` in `OnIdle` and paints the code in its draw, which encodes nothing.
- **The token is part of the URL path.** It is 16 random bytes in base64url, new for each session, compared in constant time, never stored and never logged. Anyone on the network who has the URL can read the selection while the dialog is open.
- **Only local files are shared.** Each selected entry must be a `FileSystem` URL; any other drive is refused with a reason and nothing starts. A folder whose path contains `@/` is refused, because `Url` reads that as an archive separator.
- **Paths are checked per segment.** Each segment is decoded once; a decoded `/`, `\` or NUL, a `.` or `..` segment, and a symbolic link or reparse point at any level all give 404. Dotfiles are listed and served.
- **Limits:** 8 concurrent connections (a ninth is closed), 4 concurrent file reads (503 above that), 2 s for `StopAsync` to wait for the accept loop (never for reads).
- **The QR code is hidden below 30 rows**; the URL text stays.
- **Tests:** `FileShareRequestHandlerTest` and `FileShareAddressTest` need no socket. `FileShareServiceTest` is the one unit test that binds a real port on the LAN address; it is an approved exception to the sandbox rule. The end-to-end test uses `SandboxServices.FileShareStub`, which binds nothing.
- **Encoder:** `iConsole/Qr/QrEncoder.cs` is hand-written (byte mode, level M, versions 1 to 10), checked against the published Reed-Solomon example and the level-M format strings, and decoded by ZXing.Net in `iConsole.Test` only.

### Grid queries and the AI draft (`iConsole/Tabular/`, `GridQuery`, `GridQueryDrafter`, `IGridQueryAssistant`)
The CSV, Parquet, HDF5 and SQLite viewers share one query language and one way of asking the AI model for a query. The query line
takes `SELECT`, `WHERE`, `ORDER BY` and `LIMIT` (the CSV language, see below). A line that starts with `?` asks the model: the draft
lands in the query line and runs only when the user presses Enter, like any query typed by hand.

- **The data is an N-dimensional array (`iConsole/Tabular/GridArray.cs`).** `IGridArraySource` gives the shape, the column names (a
  file's headers) or the compound fields of a one-dimensional dataset, and reads a block: a start and a count per axis, returned
  row-major with the last axis fastest. `GridArrayView` is an immutable view of such an array. `WithIndex` and `Step` fix an axis,
  `Project` and `Transpose` choose which two axes are the rows and the columns, `Describe` gives the position for the slice line
  (`[1, *, *]`), and `MaterializeAsync` reads the window shown: the rows are cut first to honour the row, column and cell caps, and
  only the cells of the window are read. The HDF5 viewer reads every dataset through `HdfGridArray` and `IHdfFileReader.ReadBlockAsync`,
  so a dataset of any rank is one view: `+ -` step the fixed axes, `[ ]` choose the active one, and `T` swaps the rows and the
  columns. CSV and Parquet rows are a two-axis `InMemoryGridArray`. Every query goes through one path, `GridArrayView.QueryAsync`: a query that names `FROM cells` (or an array of three or more axes) runs over the long form, one row per cell with an `axis0` ... `axisN` column per axis; any other query runs over the rows of a one- or two-axis array, one row per index of the first axis. Either way the array is streamed in blocks of at most `DefaultChunkCells` cells, each block is filtered as it arrives, and only matches are kept (`MaxMatches`). Order and limit run over the matches. SQLite is a relation, not an array. A SQLite `SELECT` or `WITH` is a `SqlQueryArray`: the column names and the row count come from two queries, and each block
  the view asks for is the statement with `LIMIT` and `OFFSET`, so the viewer reads only the window it shows (`PRAGMA` and other
  non-row statements keep the direct path, because they cannot be wrapped as a subquery). `FROM cells` in the HDF5 query line runs
  the query over every cell of the array (`GridArrayView.QueryLongFormAsync`): the array is read in blocks of at most
  `DefaultChunkCells` cells, each block is filtered as it arrives, and only the matches are kept (at most `MaxMatches`; the status
  line says when that cap cut them). Order and limit then run over the matches, so they hold across blocks.
- **One parser, three uses.** `GridQuery.Parse` turns the text into a `GridQueryPlan` (table, columns, a `GridCondition` of OR-joined
  AND groups, order, limit). `GridQuery.Execute` runs a plan over rows in memory; `GridQuery.Run` is the two together. `GridQuery.ToSql`
  renders the same plan as one SQLite statement. A plan is inspectable, so the AI draft and the SQLite viewer use the same checks.
- **In memory:** the CSV viewer (the whole document), the Parquet viewer (the rows it has read, `ParquetPreview.Names` for the bare
  column names), and the HDF5 viewer (the current slice). A query over these runs on a background task (`GridQueryRun`) and only the
  newest result lands on the idle pass. The status line says how many of the shown rows match.
- **SQLite:** the plan is rendered as SQL and run through the existing read-only reader, so `SqliteStatementChecker` and the read-only
  connection still apply. Every name is quoted and every value is a quoted literal, never bare: a text column holding `0123` must keep
  its zero. SQLite applies the column's own type to a quoted value, so `amount > '10'` compares as a number on an INTEGER column.
  A numeric-looking value in a text column compares by SQLite's affinity rules, not as the in-memory engine does; that is the one place
  the two differ. `NULL` ordering and `CONTAINS` (`instr(lower(...))`) are rendered to match the in-memory meaning.
- **The draft is checked before it is shown.** `GridQueryDrafter` cleans the model's reply (a code fence, a `Query:` label, a trailing `;`,
  only the first line), parses it against each table's column names, and takes the table a `FROM` names or the only table. A draft that
  names a missing column or table is refused with the reason on the status line, and the question stays in the line so it can be edited.
- **What the model sees.** `GridSchema` holds table and column names, types and row counts, never a cell value. The question is the
  user's own text. The model's answer is never run without the user's Enter.
- **AI off.** `GridQueryAssistant` (`iTerminal.Core/AI/`) reports `AI is off` for `AI:Provider = None`, and the drafter then says so
  without calling a model. The viewers take `IGridQueryAssistant` as an optional constructor argument; without one they report `AI is not
  set up`. The end-to-end suite replaces the assistant with `SandboxServices.GridQueryAssistant`, which answers a fixed query at once.
- **SQLite schema.** The draft needs the tables and columns: `GridQueryDrafter` takes the schema as a task, so the viewer reads
  `sqlite_master` and runs `SELECT * ... LIMIT 0` for each table on a worker thread, and the question waits for it.
- **HDF5 keys.** `T` swaps the rows and the columns and reads the window again, and only for a dataset of two or more axes. While the query line has focus, `+ - [ ]` type into the line, because `-` is part of a number in a query. Ctrl+arrows
  still step the slice. Stepping the slice runs the query again over the new slice.
- **Axis clauses (first-class axes in the language).** After `FROM`, a query may name axes of an array of two or more axes:
  `AT axis1 = 2` fixes an axis at an index (repeatable), `ROWS axis0` and `COLUMNS axis2` choose the axes shown. The query then reads
  the window those choices give (`GridArrayView.QueryAsync` through `QueryWindowAsync`), and its `WHERE`, `ORDER BY` and `LIMIT` run over
  that window's own columns, labelled by the remaining axis's indices. Example: `FROM cube AT axis0 = 2 ROWS axis1 COLUMNS axis2 WHERE "1" = '2.0.1'`.
  The clause is read without resolving the filter's columns (`GridQuery.AxisClauseOf`), and a query without one is unchanged. Axes
  named but not in the array, or fixed and shown at once, are refused.
- **SQLite `:` lines.** A query line that starts with `:` runs a grid query over the result on screen, through `SqlQueryArray.QueryAsync`,
  so the filter, order and limit run in one statement inside the database. Any other line stays raw SQL, run as written.
- **Cancellation.** A `FROM cells` search is cancelled between blocks when the query is replaced or the dialog closes; a block that is
  already being read finishes first.
- **Not built:** grouping and aggregates (`GROUP BY`, `COUNT`, averages), joins, saving a result, and queries over a Parquet file beyond
  the rows the viewer reads (10,000 rows, 100 columns, 100,000 cells). The model cannot average a column because the language cannot.
- **Tests:** `GridQueryTest` (parsing, the plan, in-memory and SQL rendering, quoting), `GridQueryDrafterTest` (cleaning, checking,
  overtaken and cancelled answers, the no-AI and failure paths), `GridQuerySqliteAgreementTest` (iTerminal.Core.Test: the rendered SQL and
  the in-memory engine return the same rows, through the real reader), `GridQueryAssistantTest` (the model adapter: no values are sent,
  AI off makes no call), and the viewer tests in `CsvViewerDialogTest`, `ParquetViewerDialogTest`, `HdfViewerDialogTest` and
  `SqliteViewerDialogTest`.

### CSV viewer (`CsvViewerDialog`, F3 on a `.csv` or `.tsv` file, `iConsole/Csv/`, `iConsole/Controls/CsvGrid.cs`)
F3 opens a delimited file as a grid: the header on top, the rows below, and the columns scroll sideways to keep the cursor cell on screen. The query line above the grid takes a small SQL-like query, `SELECT name, amount WHERE amount > 10 AND city = 'Pécs' ORDER BY amount DESC LIMIT 20`, and the status line says how many rows matched. F4 switches to the file's text, as in the JSON and XML viewers.

- **The record and the query are in `iConsole`, with no UI; the parser is in `iConsole.Formats`.** `CsvDocumentParser.Parse` (behind `ICsvDocumentParser`) reads RFC 4180 style quoting (a quoted field may hold the delimiter, a line break or a doubled quote). Without a delimiter it picks the candidate (`,` `;` tab `|`) that splits the first record into the most fields. A `.tsv` file is always read with tabs. A short row reads as empty in the missing columns; a blank line is skipped; a file past `MaxRows` keeps its first rows and says so.
- **`CsvQuery` is a parser that builds a predicate.** `SELECT *` or a column list, then `WHERE` (the word is optional when the query starts with a condition), then `ORDER BY` and `LIMIT`. `AND` binds tighter than `OR`; there are no parentheses. Numbers compare as numbers when both sides are numbers, otherwise text ignoring case. Errors name the column that does not exist and list the columns; a malformed query says what it expected.
- **A query runs on a background task.** `StartQuery` starts it and returns; `OnIdle` puts the result in the grid when it lands, and only the newest query may land. A query that fails to parse keeps the rows already on screen and shows its error on the status line.
- **The file is read with `TextEncodings.Decode`** (a byte order mark, else UTF-8 when valid, else the system's ANSI code page), never as lossy UTF-8, so a Windows-1250 file shows its accents. **F8 / Shift+F8** read the grid again under the next / previous code page of `TextEncodings.Available`; the title names the current one. `Encoding` changes at once (quick presses keep advancing), the re-read runs on a background task (`SwitchTask`) and lands on the next `OnIdle`; it drops a query that was running and skips the byte order mark. F8 is always handled, so it never falls through to Delete in the panels or Shift+F8 to the settings, but it does nothing while the file is unread or the F4 text view shows. **The F4 text view always picks its own code page** (`FileToTextConverterService`, as in the F3 viewer) and does not follow the grid's.
- **`CsvGrid` takes the keyboard only while enabled and focused**, lists its keys for the help screen, takes the focus on a click, and repaints its whole rectangle. Column widths come from the header and the first 500 rows, within 3 to 40 cells.
- **Space marks a row and moves down** (`CsvGrid.MarkedRows`). Every `CsvGrid` viewer gets the marks and a two-cell mark column at the left (`*`); only the CSV viewer exports them. A new query, a re-read or a code-page change clears the marks, because they belong to the rows on screen.
- **F2 exports the grid** (`CsvExportCommand` → `CsvViewerDialog.ExportAsync`, `iConsole/Csv/CsvExport.cs`, `iConsole/Csv/TableExport.cs`): the marked rows in grid order, or every row on screen when none is marked, with the grid's current columns. A selection prompt asks for the format (CSV is the default, then JSON and XML). A second prompt asks for the file name, which starts as `<file stem>-export.<extension>`; the format's extension is appended to a name that does not already end in it (ignoring case), so `report` becomes `report.csv`. A name with a folder in it is refused, and the file goes in the folder of the viewed file. An existing file is replaced only after a yes; a no or a cancelled question writes nothing. The CSV is RFC 4180: a comma delimiter even for a `.tsv` source, CRLF line ends, a header row. JSON is an array of objects keyed by the column names; XML is `<table>` with a `<row>` per row and a `<c name="…">` cell per column. JSON and XML values are text, as the CSV is. An empty column name becomes `column_N` (N its 1-based position), and a repeated name gets `_2`, `_3` and so on (ordinal, case-sensitive). XML drops the characters XML 1.0 cannot carry. Every format is UTF-8 without a byte order mark. A read-only folder is refused before the prompt. The command does not await the question (the input thread answers it), so the export reports its own failures. The export of a file over `CsvDocument.MaxRows` contains the rows read, and the status line says so.
- **Tests:** `CsvExportTest` (the writer), `TableExportTest` (JSON, XML, the column names and the extension rule), `CsvGridTest` (marks and the mark column), `CsvViewerExportTest` (the export, the format and name prompts, and their answers) and `EditorViewerAndPermissionsTest` (F2 in the real app: the format, Enter, the suggested name, the file on disk).
- **Tests:** `CsvDocumentTest` (parsing), `CsvQueryTest` (the language), `CsvViewerDialogTest` (loading, queries, the status line, `.tsv`, F8 code pages), `EditorViewerAndPermissionsTest` (F8 and Shift+F8 in the real app) and `ViewFileDialogCommandTest` (F3 routes `.csv` and `.tsv` here).
- Not built: sorting by clicking a header, saving a query's result, and parentheses in the condition.

### XPS viewer (`XpsViewerDialog`, F3 on a `.xps` or `.oxps` file, `iConsole/Xps/`)
F3 opens an XPS or OpenXPS package with a picker of its pages on top and the selected page's text below it. The status line says which page is shown, its size in device-independent pixels and the package's title; Esc closes it. Up and Down step the picker, and **Ctrl+Left / Ctrl+Right** step the pages from anywhere in the dialog.

- **The package reader is in `iConsole.Formats`** (`XpsDocumentReader`, behind `IXpsDocumentReader`). The `XpsDocument` and `XpsPage` models are in `iConsole`, with no UI. `XpsDocumentReader.Read` turns the zip into pages: each page's number in the package, its number in its own document, its `FixedPage` width and height, and its text. A page's `Glyphs` runs are sorted by origin into lines, and runs on one line are joined with spaces. A page drawn only with shapes or pictures has no text, and the viewer says so rather than showing a blank pane.
- **Sequence discovery.** The sequence is the target of the package's root `fixedrepresentation` relationship in `_rels/.rels`, whatever the part is called (`FixedDocSeq.fdseq` is the usual name). When the relationship is missing, the shortest `.fdseq` part is used. References are resolved against the referring part, so both `Pages/1.fpage` and `/Documents/1/Pages/1.fpage` work.
- **OpenXPS reads the same.** Every lookup matches on the XML local name and never on a namespace, so the OpenXPS namespace (`http://schemas.openxps.org/oxps/v1.0`) needs no special case; XPS and OpenXPS both open here.
- **A package that cannot be read says why.** `XpsDocumentReader.Read` never throws: a malformed zip, a missing sequence or an unreadable part comes back in `Error` and the dialog shows that text on the status line. A package that holds no pages says so. The text reader keeps the same `[Could not read the document: …]` output for the generic viewer and the index.
- **The file is read as bytes on a worker thread.** The zip and XML parsing run on `Task.Run`, and only the model updates return to the thread that owns the controls. The dialog instance is registered as transient and reset on every open, so nothing of the last package survives.
- **Routing and menus.** F3 from a panel, File > View and the right-click View all route `.xps` and `.oxps` here, from any drive; other files keep the generic viewer. `XpsTextReader` still serves the generic F3 text path and document indexing.
- **Tests:** `XpsDocumentTest` (page order, sizes, numbering across documents, relationship and fallback discovery, OpenXPS, properties, failures, extensions), `XpsViewerDialogTest` (loading, page stepping, the keyboard, multi-document labels, no pages, broken and unreadable files, reuse), `XpsTextReaderTest` (the flat text and a real package), `ViewFileDialogCommandTest` (the routing) and `EditorViewerAndPermissionsTest` (F3, Ctrl+Right, Ctrl+Left and Esc in the real app).
- Not built: rendering the page itself (shapes, images and fonts), text search, zoom, printing and editing.

### Certificate viewer (`CertificateViewerDialog`, F3 on a local `.pem`, `.crt`, `.cer` or `.der` file, `iConsole/Certificates/`, `iConsole/Dialogs/`)
F3 opens a certificate file with a picker of its certificates on top and the selected certificate's fields below: subject, issuer, validity, serial, public key, signature, alternative names, CA flags, key and extended key usage, and the SHA-256 and SHA-1 fingerprints. The status line gives the position, whether the certificate is valid, expired or not yet valid, and how many private-key blocks were not shown. Esc closes it (a focused picker that is open takes the key first); Up and Down step the certificates.

- **Only a file that holds a certificate opens here.** Anything else falls back to the text viewer, as before: a `.der` that is not a certificate (a key, a CRL, a CSR), a `.pem` with no `CERTIFICATE` block, a file over 1 MiB, a file that cannot be read. A `.pem` with a certificate and a private key opens here, with the key counted on the status line and its material never shown.
- **Local files only.** Remote, SSH, archive, memory and recycle files open as text. The viewer reads the path with `FileStream`, capped at 1 MiB; a larger file is an overflow and falls back.
- **The command does not read.** `ViewFileDialogCommand` sets the dialog's `Url` and its `OnFallback`, then shows the dialog and returns. The read runs on a worker thread that `LoadTask` exposes; the result is applied once, on the UI thread, from `OnIdle`. A fallback closes the dialog itself, then runs `OnFallback`, which calls `ShowTextViewerAsync`. A fallback that throws is reported as an error notification, not lost.
- **One instance, shared.** The dialog is transient but the panel factory is a singleton, so one instance serves every panel. Setting `Url` starts a new request and clears the run-once flag; the parsed result is kept for one `FullUrl` together with the file's length and last-write time, so a second F3 on an unchanged file does not read it again, a file edited on disk is read again, and a different file never shows the last one's certificates. A result for an earlier URL is never applied. Esc cancels a read in progress; a `FileStream` read cannot be interrupted, so the result is only dropped.
- **Special files.** A FIFO or device passes the `File.Exists` check, and the read then blocks a worker thread that Esc cannot release. The text viewer already reads these files with `File.ReadAllBytes`, so F3 on one already blocks today; the viewer keeps the file in its loading state instead of showing the text. This is a known limitation, not fixed here.
- **Parsing.** `CertificateReader` (`iConsole.Formats/Certificates/`, behind `ICertificateReader`) splits the PEM blocks itself and passes each certificate's DER to `X509CertificateLoader`; a DER file is one certificate. Only `CERTIFICATE` blocks count. Strings from the certificate have control characters replaced before they reach the screen. The validity line is judged against the dialog's `Clock`, which a test pins.
- **Tests:** `CertificateReaderTest`, `CertificateFileReaderTest` (iConsole.Formats.Test), `CertificateFormatterTest` (iConsole), `CertificateViewerDialogTest` (iConsole: fields, fallbacks, run-once, reuse, cache, stale results, Esc during loading), `ViewFileDialogCommandTest` (routing with a mocked `IControl`, including the fallback and the remote case), `MenuShortcutTest` (File > View on a `.pem`), and `EditorViewerAndPermissionsTest` (F3 on a certificate, F3 on a key file, in the real app).

### SQLite viewer (`SqliteViewerDialog`, F3 on a local `.sqlite`, `.sqlite3`, `.db` or `.db3` file, `iConsole/Sqlite/`, `iConsole.Formats/Sqlite/`)
F3 opens a database as a result grid. The query line takes one SQL statement, run read-only; Enter runs it, and F4 switches to the result as aligned plain text. The default query lists the schema (`sqlite_master`), so a database opens on its tables, views and indexes.

- **The UI has no SQLite dependency.** `iConsole` holds the contract (`ISqliteDatabaseReader`, `SqlTable`, `SqlQueryException`), the file-type check and the text formatter. `iConsole.Formats` holds `SqliteDatabaseReader`, which uses `Microsoft.Data.Sqlite` (pinned in `iConsole.Formats.csproj`; `iTerminal.Core` references the same version for EF Core).
- **Three write guards, all of which must be bypassed to write.** (1) `SqliteStatementChecker` runs first: it masks comments and the inside of quotes and brackets, so a `;` or `/*` in a string is never code. Only one statement is allowed, and its first keyword must be `SELECT`, `WITH`, `EXPLAIN` (checked recursively) or `PRAGMA`. A `PRAGMA` must be one of a fixed list of read-only names and may not contain `=`. (2) The connection opens `Mode=ReadOnly`. (3) `PRAGMA query_only = ON` is set after open. `WITH ... DELETE` is valid SQLite and passes the checker; the read-only connection is what stops it, and a test checks the file's bytes are unchanged afterwards. Extension loading stays off.
- **Limits.** A query may run for 10 seconds (`CommandTimeout`), which also applies to a legitimate large query; a longer one shows the error on the status line. Results keep at most 10,000 rows and say so. A `NULL` cell reads `NULL`, a blob reads `<blob N bytes>`.
- **Cancellation is a native interrupt.** `SqliteCommand.Cancel` does not stop a running step, so cancelling the token calls `sqlite3_interrupt` on the connection's handle. A newer query or closing the dialog cancels the running one; a cancelled or stale result is dropped on the idle pass and writes nothing to the status line.
- **Files.** Only a local file opens here: a remote `.db` falls through to the generic text viewer, as before. The first 16 bytes must read `SQLite format 3` followed by a NUL, so a non-SQLite `.db` file, and an empty one, is refused with "Not an SQLite database" before SQLite opens it. A WAL database opens when its `-wal` file is present; if SQLite cannot open it, the error appears on the status line.
- **Tests:** `SqlTableTextTest` and `SqliteViewerDialogTest` (iConsole), `SqliteStatementCheckerTest`, `SqliteDatabaseReaderTest` (temp files only, including the WITH-DELETE byte check, a runaway query cancelled promptly, and WAL), and `ViewFileDialogCommandTest` (F3 routes a local database here and leaves a remote one alone).
- Not built: writing to the database, a table browser, export, and multi-statement scripts.

### HDF5 viewer (`HdfViewerDialog`, F3 on a local `.h5`, `.hdf5`, `.hdf` or `.he5` file, `iConsole/Hdf/`, `iConsole.Formats/Hdf/`)
F3 opens an HDF5 file with a picker of its datasets on top and the chosen dataset as a grid under it. A dataset of three or more dimensions shows one 2-D slice: the last two dimensions are the rows and the columns, and the leading dimensions are fixed by an index you step through. Esc closes it.

- **What the grid shows.** A scalar is one cell. A one-dimensional dataset is a `#` (index) column and a `value` column; a one-dimensional compound dataset has one column per field. A two-dimensional dataset is its matrix, the column numbers as headers. Beyond that, `[i, j, *, *]` is the slice at leading indices `i, j`. The row number is a literal first column (`CsvGrid` has no row-header column), so stepping the slice puts the cursor back at the top left (`SetData` resets it).
- **Keys.** `+` / `-` step the index of the active leading dimension, `[` / `]` choose which leading dimension is active, clamped at both ends (a slice line under the grid says `Slice [<1>, *, *]  dimension 1 of 1, index 2 of 3`). The typed keys are matched on the *character*, never `!Shift`: `+` is Shift+= on some layouts, and `[` and `]` are AltGr chords (Ctrl+Alt) on Hungarian, German, French, Spanish and Italian ones (`ConsoleKeyHelper.IsTypingKey`). `=` and the numpad +/- work too. **Ctrl+Up / Ctrl+Down** (index) and **Ctrl+Left / Ctrl+Right** (dimension) need no typing key at all. They do nothing for a dataset of two dimensions or fewer, and while the dataset list is open. The dialog handles them before its children, so the focused picker never sees them.
- **The UI has no HDF dependency.** `iConsole` holds the contract (`IHdfFileReader`, `HdfDatasetInfo`, `HdfSlice`, `HdfReadException`), the file-type check (`HdfFileTypes`) and the dialog. `iConsole.Formats` holds `Hdf5FileReader`, which uses PureHDF 2.2.0 (MIT, managed, no native library; pinned in `iConsole.Formats.csproj`).
- **Only the slice is read.** `ReadSliceAsync` reads a hyperslab, so a slice of an 18 MB dataset allocates a few hundred kilobytes (a test checks it). The caps are 10,000 rows, 100 columns and 100,000 cells (the rows are cut first), and the status line says which axis was cut. The dataset list is capped at 5,000 and says so. PureHDF cannot cancel a read already running, so cancellation is checked between steps and a slice is bounded by the caps.
- **Values read as text.** Integers as stored, floats as the shortest text that reads back the same (`NaN` and `Infinity` as words), strings (fixed- and variable-length) as text, an enumeration or a bit field as its stored integer (not by name). A compound element in a 2-D or larger dataset reads `{a=1, b=2}`. A reference, opaque, array or time type shows `<unsupported type>` in every cell.
- **Files.** Only a local file opens here; a remote one falls through to the generic text viewer. The HDF5 signature must be at the start of the file or after a user block (512 bytes and every doubling), so an HDF4 `.hdf` file (a different format), a non-HDF file and an empty one are all read by the generic viewer, as before. A group whose links cannot be followed is skipped, and the walk stops at depth 32 and 20,000 groups.
- **Threading.** The listing and each slice run on `Task.Run`, results land on the next `OnIdle`, and only the newest slice may land (a generation counter, as in the SQLite viewer); `Close()` cancels both. A read that fails keeps the grid and shows its message on the status line.
- **Menu and panels.** File > View, the panel's F3 and the right-click View all route here (the SQLite viewer is not threaded through the File menu's View item, an existing gap this change leaves alone).
- **Tests:** `HdfFileTypesTest`, `HdfViewerDialogTest` (a fake reader: slices, the keys on several layouts, clamping, stale results, failures, close, 5,000 datasets), `Hdf5FileReaderTest` (files written by PureHDF's own writer, cell by cell), `ViewFileDialogCommandTest` (the routing) and `EditorViewerAndPermissionsTest` (F3, `+`, Ctrl+Up and `-` in the real app). `Test/FileTypes/example.h5` is a sample file.
- The query line runs the grid query language over the slice shown, and a `?` line asks the AI model (see *Grid queries and the AI draft*). Not built: HDF4, writing, attributes, choosing which axes are the rows and columns, scrolling past the row cap, an F4 text view, export.

### Markdown, YAML, TOML and dBase viewers (`MarkdownViewerDialog`, `YamlViewerDialog`, `TomlViewerDialog`, `DbfViewerDialog`, `iConsole/Markdown/` (model and layout), `iConsole.Formats/Markdown/` (parser), `iConsole.Formats/Yaml/`, `iConsole.Formats/Toml/`, `iConsole/Dbf/`, `iConsole/Structured/`)
- **Markdown** (`.md`, `.markdown`, from any drive) is parsed by `MarkdownParser` (`iConsole.Formats/Markdown/`, behind `IMarkdownParser`, on the UI thread as before) and drawn by `MarkdownViewerControl`, which takes the parsed blocks through `SetDocument`. F4 shows the file's text and back again.
- **YAML and TOML** (`.yaml`, `.yml`, `.toml`) open as a tree, the same rows as the JSON viewer, through the shared base `DataTreeViewerDialog`. A subclass names the format and parses the text on a background task. A file the parser rejects shows the parser's message on the status line and opens as its text. YAML plain scalars take the YAML 1.2 core types; a multi-document file reads as an array. An alias that points at its own parent is refused, and a file nested deeper than 256 levels or with more than 200,000 values is refused too.
- **dBase** (`.dbf`) reads the header, the field descriptors and the live records with `DbfDocument.Parse`. The records show in a `CsvGrid`. The code page comes from the header's language byte, and a deleted record is counted, not shown. A memo's text lives in a `.dbt` file that is not read, so a memo shows as `(memo)`.
- **Tests:** `MarkdownFileTypesTest`, `MarkdownViewerDialogTest`, `YamlDocumentReaderTest`, `TomlDocumentReaderTest`, `DataTreeViewerDialogTest`, `DbfDocumentTest`, `DbfViewerDialogTest` (iConsole) and the routes in `ViewFileDialogCommandTest`. Examples: `Test/FileTypes/example.md`, `example.yaml`, `example.toml`, `example.dbf`.

### HAR viewer (`HarViewerDialog`, F3 on a local or remote `.har` file, `iConsole/Har/`)
A HAR file (HTTP Archive 1.2, the network log a browser's developer tools export) opens as a grid with one row per request: number, method, status, media type (without its parameters), size, time in milliseconds and URL. Enter opens the row's request and response: the request line, status, start time and timings (only the phases the file measured), the query string, the request and response headers, and both bodies. A JSON body is pretty-printed, a base64 text body is decoded, and any other body is named by its size and not shown. Backspace goes back to the list; F4 shows the file's own text, and F4 again goes back to the list. Esc closes it.

- **The parser has no UI.** `HarDocumentParser.Parse` (`iConsole.Formats/Har/`, behind `IHarDocumentParser`) reads the file with `System.Text.Json` and keeps each entry's own JSON as a `JsonElement`, so a large log does not copy every body up front; the details are built when a row is opened. A file with no `log.entries` list throws `InvalidDataException`, which the dialog reports on the status line (`Not a HAR file: ...`), and the F4 text is still there. HAR's `-1` means unknown, so a size of `-1` shows as `-`.
- **Bounds.** The list is capped at 100,000 requests and says so; a body is shown up to 100,000 characters and the cut is named in the text.
- **Routing.** `ViewFileDialogCommand` sends a `.har` file here before the JSON viewer, from any drive, because the file is read through the file system like JSON. The dialog is registered in `iTerminalApplicationBuilderExtension` and threaded through the panel, the File menu, the status-bar hot keys and `ContentBrowser`, like the other F3 viewers.
- **Tests:** `HarDocumentTest` (parsing, sizes, the errors, the file ending), `HarEntryDetailsTest` (the sections, JSON, base64, binary bodies, timings, the cut) and `HarViewerDialogTest` (the list, the status line, Enter, Backspace and F4). `ViewFileDialogCommandTest` checks the routing. `Test/FileTypes/example.har` is a sample file.
- Not built: writing or editing a HAR file, filtering or sorting the list, the page timings, and HAR 1.1 or the browser-specific extensions beyond the fields above.
### Parquet viewer (`ParquetViewerDialog`, F3 on a local `.parquet` or `.parq` file, `iConsole/Parquet/`, `iConsole.Formats/Parquet/`)
F3 opens a Parquet file as a grid of its first rows. The headers are the column names with their types (`id (int64)`), and the status line gives the row and column counts and says which axis was cut. Read-only; Esc closes it.

- **The grid shows the start of the file only.** The caps are 10,000 rows, 100 columns and 100,000 cells (the rows are cut first to honour the cell cap). Only the row groups that hold the rows shown are read, and only the top-level columns shown are decoded. A row group is read whole, so a file with very large row groups costs what one group holds.
- **Values read as text.** Integers and floats in invariant text, booleans as `True` or `False`, dates as `yyyy-MM-dd` (with the time when there is one), strings as they are. A null is an empty cell. A nested column (a list, a struct or a map) shows `<nested>` in every row, and a binary column shows `<unsupported type>`; their values are not decoded.
- **The reader has no UI dependency.** `iConsole` holds the contract (`IParquetFileReader`, `ParquetPreview`, `ParquetReadException`), the file-type check (`ParquetFileTypes`) and the dialog. `iConsole.Formats` holds `ParquetFileReader`, which uses Parquet.Net 6.1.0 (MIT, managed, no native library; pinned in `iConsole.Formats.csproj`). Its namespace is `iConsole.Formats.Parquet`; the interface it implements stays in `iConsole.Parquet`.
- **Files.** Only a local file opens here, and its first four bytes must be `PAR1`. An encrypted file (`PARE`), or a `.parquet` file that is not Parquet, is read by the generic viewer, as before.
- **The read runs on a worker thread.** `ViewFileDialogCommand` checks the magic on `Task.Run` and sets the dialog's `Url`; the dialog reads on another `Task.Run` and lands the result on the next idle pass. A read that is cancelled by closing the dialog lands nowhere.
- **Tests:** `ParquetFileTypesTest`, `ParquetViewerDialogTest` (a fake reader: the headers and cells, the cut notices, an empty file, failures, close, reopening), `ParquetFileReaderTest` (files written by Parquet.Net, with nulls, several row groups, the three caps, nested columns and failures, plus `Test/FileTypes/example.parquet`, which Apache Arrow wrote), and `ViewFileDialogCommandTest` (the routing). `Test/FileTypes/example.parquet` is a sample file.
- The query line runs the grid query language over the rows shown (see *Grid queries and the AI draft*); a `?` line asks the AI model. Not built: nested values (the list, struct and map contents), writing, schema or statistics pages, row-group or page navigation, and encrypted files.

### Executable analysis (`ExecutableAnalysisDialog`, F3 on a local PE, ELF or Mach-O file, `iConsole/Executables/`, `iConsole.Formats/Executables/`)
F3 on a Windows (PE), Linux (ELF) or Apple (Mach-O) executable, library or driver opens a read-only analysis instead of the text viewer. It is a two-column grid (item, value) in sections: General (format, architecture, kind, platform or subsystem, build time, entry point), Hardening, Imports, Exports, Sections (size in the file, entropy, access), Signature and Hints. Esc closes it. **The file is parsed, never run or loaded.**

- **Which files.** Only a local file. The name is a first filter (`ExecutableFileTypes.IsCandidate`: a known ending such as `.exe .dll .sys .so .dylib .o`, a versioned `libx.so.6`, or no ending at all, so Linux programs open); the magic bytes decide (`HasSignature`: `MZ`, `0x7F ELF`, the four Mach-O magics, and `0xCAFEBABE` only with 1 to 30 slices, so a Java class is not claimed). The 8-byte read runs on a worker thread in `ViewFileDialogCommand.ExecuteAsync`, like the SQLite and HDF5 routes. A file that fails the check (a script called `.exe`) goes to the generic viewer as before; a file that passes but cannot be parsed still opens the dialog, which says why on the status line (F4 is still the raw route). `.bin` is excluded by `CanExecute` as before.
- **The UI has no parser.** `iConsole` holds the contract (`IExecutableAnalyzer`, `ExecutableReport` and its records, `ExecutableAnalysisException`), `ExecutableFileTypes` and the dialog; `iConsole.Formats/Executables` holds the parsers (internal) behind the public `ExecutableAnalyzer`. No package: PE headers come from `System.Reflection.PortableExecutable.PEHeaders`; the import, export and load-configuration tables, ELF and Mach-O are walked by hand.
- **Hostile files.** Every read goes through `BoundedFileReader`, which checks offset and size against the file and returns "no" instead of throwing; counts and tables are capped (512 PE sections, 1,024 import descriptors, 65,536 thunks per library, 256 ELF program headers, 2,048 section headers, 4,096 Mach-O load commands, 1,000,000 symbols, 4 MB string tables, a 1 MB certificate blob) and a cut list says so as a hint. Entropy is read in 64 KB chunks, at most 32 MB per section and 256 MB per file (a bigger section is marked sampled with `*`), checking cancellation between chunks. Anything the parser still trips over comes out as `ExecutableAnalysisException`, never another type.
- **Hardening.** PE: ASLR (dynamic base and relocations present), high-entropy ASLR (64-bit only), DEP/NX, CFG, SafeSEH (x86 only: the handler table, or "no SEH"), `/GS` cookie from the load configuration. ELF: PIE (ET_DYN with an interpreter or `DF_1_PIE`), non-executable stack (`PT_GNU_STACK`), RELRO (`PT_GNU_RELRO`), full RELRO (`BIND_NOW`), stack canary (`__stack_chk_fail` in a symbol string table; unknown when there are no symbol tables). Mach-O: PIE, no-heap-execution, no-stack-execution, stack canary (`___stack_chk_fail`). A flag that does not apply or cannot be told shows as "n/a or unknown", never as "no". The flag names are technical terms and are not translated; every other label is in the six `.resx` files (`Exe.*`).
- **Packer hints.** A section with entropy above 7.2 and at least 256 bytes in the file, a section name a known packer or protector uses (UPX, ASPack, Themida, VMProtect, MPRESS...), a section that is writable and executable, very few imports in a native executable, a `UPX!` signature in an ELF or Mach-O header, a build time stamp that is zero or in the future (not shown).
- **Signature.** A PE's certificate table is read in-process (`AuthenticodeCertificates`, `System.Formats.Asn1`, no `System.Security.Cryptography.Pkcs`): subject, issuer, validity and thumbprint of each certificate in the SignedData. It is labelled "not verified": nothing about the chain, the file hash or revocation is checked. `Services/Signing` (`SignatureService`) does not fit this read-only in-process view (it shells out to PowerShell, is Windows-only and verifies the whole file), so verification stays in the signing dialog. A Mach-O reports only that a code signature load command exists.
- **Mach-O.** A thin file, or the first readable slice of a universal binary (all slice architectures are listed).
- **Disassembly (F5 or the Disassemble button).** The dialog swaps its grid for a `TextViewerControl` listing of the file; F5 again swaps back. The listing is built by `ExecutableDisassembly.Build` (`iConsole.Formats/Services/Disassembly/`), the same builder the F3 text viewer uses, so the IL, native sweep, limits and notes are the ones described in media-and-documents. It is built on the first show of a file, on a worker, and kept until the file changes; a file over 256 MB gets a note instead of being read. `Close()` and a reset cancel a build that is running.
- **Threading.** The analysis runs on `Task.Run`, lands on the next `OnIdle`, and a generation counter drops a result that was replaced or whose dialog closed; `Close()` cancels the token, and the source is disposed only once the read has finished. The listing follows the same pattern.
- **Menu and panels.** F3 (panel, File > View, right-click View) route here.
- **Tests:** `ExecutableFileTypesTest`, `ExecutableAnalysisDialogTest` (fake analyzer: rows, worker thread, failures, close, reopen, wrapping, F5 listing: toggle, build once, worker thread, failure, cancel on close, reset on another file), `ExecutableDisassemblyTest` (a real .NET assembly on disk), `ExecutableAnalyzerTest` (files built byte by byte in `ExecutableFixtures`: each flag on and off, big-endian ELF, universal Mach-O, a generated certificate, the real CoreLib and host program, every truncation length and 500 seeded corruptions per format under 20 s (a hang detector, not a benchmark: a virus scanner or a busy machine can delay opening a temp file by seconds) and 64 MB, huge counts, cancellation), `ViewFileDialogCommandTest` (routing) and `EditorViewerAndPermissionsTest` (F3 on an ELF, F5 on it, and on a truncated one in the real app).
- Not built: validating the signature chain, Mach-O code-signature certificates, delay-load and bound imports, resources and version info, an F4 hex route from the dialog, export.

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
- **Two backends, picked by the operating system in DI.** On Linux `LibvirtVirtualMachineService` runs `virsh -c qemu:///system` (a CLI, like the docker drive, so nothing native is packaged); console opens `virt-viewer` detached. On Windows `HyperVVirtualMachineService` runs the Hyper-V cmdlets in Windows PowerShell 5.1 (`Get-VM`, `Start-VM`, `Stop-VM`, `Stop-VM -TurnOff`, `Restart-VM`, `Suspend-VM`, `Resume-VM`, `Save-VM`); console opens `vmconnect localhost <name>` detached. `VirtualMachineManagerDialogCommand.IsSupported` (Linux or Windows) gates the command, the hot key and the Tools menu entry; on macOS none of them exist.
- **Hyper-V details.** A name is embedded only as a single-quoted literal and the machine is found with `Where-Object { $_.Name -eq … }` that must match exactly one, because `Get-VM -Name` reads `*` and `?` as wildcards. PowerShell's messages are localised, so the script writes a fixed marker (`HYPERV_MISSING`, `HYPERV_DENIED`, `HYPERV_NOVM`) and `Describe` turns it into a sentence. A `Saved` machine is shown as shut off (Start resumes it); any `…Critical` state is treated like a crash (power-off and console only); in-between words such as `Starting` offer no action. The user must be an administrator or in the *Hyper-V Administrators* group; the service does not elevate. **The Hyper-V commands are written from the cmdlets' documentation and have not been run against a real host**; the unit tests fake the runner.
- **Keys:** S starts; Enter starts a shut-off machine and does nothing otherwise; D shuts down, R restarts, Shift+X powers off and V saves state, all with a y/n in the status line; P pauses or resumes; C opens the console; F5 lists again. The dialog has no text box, so letters are commands and `ListBox.EnableTypeAhead` stays off. Enter is claimed by the dialog ahead of the list.
- **The rule that makes the list safe:** `virsh` runs on a background task and stores its answer under a lock; `OnIdle` applies it on the UI thread. The list is read again every 5 s while the dialog is open.
- **Failures are sentences.** A run that outlives the runner's 30 s limit says the hypervisor may still be working and to press F5 (one sentence for both backends); a refused libvirt connection names the `libvirt` group, a refused Hyper-V call the *Hyper-V Administrators* group, and a missing Hyper-V module says to turn the feature on.
- **The end-to-end suite registers `SandboxServices.VirtCli`** behind `LibvirtVirtualMachineService` on every operating system: a recorded `virsh` that never reaches a hypervisor, and a `virt-viewer` stand-in that records the console instead of opening one. The dialog flow is the same for both backends, so the Hyper-V service is covered by unit tests only and no test asks the machine it runs on for its Hyper-V machines.
- Not built: snapshots, create and delete, CPU and memory columns, remote Hyper-V hosts.

### The user and group manager (`UserGroupManagerDialog`, Tools > User and group manager, `Services/UsersAndGroups/`)
- **Local accounts on Linux and Windows only.** Linux uses `getent -s files`, `useradd`, `usermod`, `userdel`, `chpasswd`, `groupadd`, `groupmod` and `groupdel`; Windows uses the LocalAccounts PowerShell cmdlets, with ADSI to rename a local group. No domain or directory accounts are listed or changed. The service is injected, and the end-to-end app replaces it with an in-memory sandbox.
- **Tab switches lists.** `/` filters; N adds a user or group, E edits a user's full name or renames a group, and D deletes the selected account or group after confirmation. On a selected user, P sets the password, A adds group membership and R removes it; each membership action asks for a local group name. The password prompt uses `PasswordBox`.
- **Password transport.** The password is never part of process arguments or PowerShell source: Linux `chpasswd` and Windows PowerShell read it from redirected stdin. Passwords cannot contain line breaks or NUL, are not persisted or logged, and are not included in status text. Linux user creation rolls the new account back if setting its initial password fails.
- Account-changing commands run asynchronously through `IExternalProcessRunner`; the dialog applies results and refreshes lists on its idle pass. Errors, including permission failures, remain visible. The service does not elevate: restart iTerminal with suitable privileges when required.
- **Tests:** `LinuxUserGroupServiceTest`, `WindowsUserGroupServiceTest`, `UserGroupManagerDialogTest` and `DialogsTest.UserGroupManagerCreatesRenamesAndDeletesSandboxAccounts`. No end-to-end test invokes the host's account tools.

### The screen saver (Settings tab 8, `SettingsScreenSaverTabPage`, `iTerminal.Core/ScreenSavers/`)

- **Settings.** The effect (`ScreenSaverEffect`: `Starfield` by default, or `Off`, `MatrixRain` or `Clock`) and the idle time in minutes (`ScreenSaverIdleMinutes`, 5 by default, 1 to 240). Both are read live by the overlay, so a change applies to the next idle period.
- **Idle time.** `IUserActivityService` (iConsole) keeps the monotonic time of the last key press or click. Both input consumers record it; pointer hover does not count. Read it through `ITimeProvider`, so tests move the clock with `VirtualTimeProvider`.
- **The overlay (`ScreenSaverOverlay`).** A full-screen control added last on the terminal screen, like the tooltip host, so it is drawn over everything. It is switched on and off on the render thread, from `OnIdle`, which reads the settings at most twice a second. The control tree changes only there and on the input thread when the screen saver is woken (`Hide`, which also repaints what was covered). Its animation task only steps the effect and calls `Refresh`.
- **Waking.** While it runs the overlay is the modal control, so the first key or click wakes it and is used up. A hover only is absorbed without waking it, so the mouse under the screen does not reach the panels.
- **It never covers a shell.** It does not start while `ConsoleOwnership.IsLent` (Ctrl+O, an editor, ssh): the child has to see the keys and paint its own screen.
- **Effects** implement `IScreenSaverEffect` and paint into a `ScreenSaverCanvas` that is cleared before each frame. A canvas of a new size rebuilds the effect's state.
- **Tests:** `ScreenSaverOverlayTest` (start after the idle time, wake on a key, hover absorbed, no start while lent, drawing), `ScreenSaverEffectsTest`, `ScreenSaverSettingsTest`, `UserActivityServiceTest` and the activity cases in `TerminalInputConsumerJobTest`.

### System information (`SystemInfoDialog`, Ctrl+F11, `Services/SystemInfo/`)
Tools > System information shows the processor (model, logical processors, load), the graphics cards (name, driver, video memory, load), physical memory with a use gauge and trend, the battery or batteries, the drives, disk I/O and network I/O rates, the operating system, runtime, current user and environment variables.

- **The dialog probes nothing.** It takes a handle from `ISystemSampler` in `OnInitializeAsync` (`Watch()`, the same pattern as the stock browser's watch loop) and drops it in its overridden `Close()`, which calls `base.Close()` and is idempotent. `OnIdle` reads `ISystemSampler.Current`, an immutable `SystemSnapshot` the sampler publishes under a lock, and rebuilds the text only when the reference changed, so the scroll position survives. The memory strip is fed from the snapshot's memory figures, not from its own sampling.
- **The sampler is a singleton service.** `SystemSampler` runs its loop only while a handle is held (the first `Watch()` starts it, the last dispose stops it) and does not wait for it on `Dispose`, so a probe that is still hung cannot hold up shutdown. It waits for the terminal to have focus before each pass, and the first pass after a focus wait or a restart reports "measuring" for every rate, so no rate is taken across a gap.
- **Probe units.** Every value is a `ProbeUnit`: one call at a time, with a budget (2 s for live values, 10 s for identity). A call that outlives its budget is abandoned: its unit reports "not answering" and starts nothing new until the call returns; its late result is discarded and the next pass asks again. Each drive, disk and network interface has its own unit, so one device that does not answer blanks only itself. Units live on the sampler, not in the loop, so a hung call is not repeated when the loop restarts. Identity (operating system, processor model, graphics identity and so on) is read until each value answers, without holding up the pass.
- **Every unit in a pass starts together**, so a pass takes one budget, not the sum of them.
- **Words are not stored.** The snapshot holds state values (`ProbeState`: ok, not reported, not answering, measuring) and raw figures. The dialog formats every word through the localizer and listens to `ILocalizer.LanguageChanged` to re-format the current snapshot; a language change never probes the machine again.
- **Platforms.** Linux reads `/proc`, `/sys/class/drm` and `/sys/class/power_supply` (and `pci.ids` for names when present); disks are whole disks from `/proc/diskstats` that have a `/sys/block` entry, with loop, RAM, compressed RAM and optical devices left out. Windows uses kernel32 (`GetSystemTimes`, `GlobalMemoryStatusEx`, `GetSystemPowerStatus`, `DeviceIoControl` on `\\.\PhysicalDriveN` opened with no access rights, so no elevation), the registry for processor and graphics identity, and PDH for GPU load through English counter names. Network counters come from `NetworkInterface` on both platforms. Battery health is not reported on Windows (the power-status API has no capacities); GPU load is not reported on NVIDIA, which has no sysfs or PDH counter here, and no `nvidia-smi` is run.
- **Tests.** `SystemInfoParsersTest` checks every format against fixture text on any platform. `SystemSamplerTest` checks the loop's start and stop, one call per hung unit, one hung disk not blanking the others, one budget per pass, the restart rule and identity reads, with a fake clock that holds only the sample interval. `SystemInfoDialogTest` checks the placeholder, one row per label, the handle, the language change and the memory strip with a fake sampler. The Windows probes are compiled on Linux but not run; the `windows-latest` job of the build runs the whole suite, including `DialogsTest`, on Windows.

### Viewer and editor plug-ins (`iConsole/Viewers/`, F3 and F4)
F3 and F4 offer the file to plug-ins before the built-in code. `IFileViewer` and `IFileEditor` both extend `IFileHandler`: `Priority` (higher first, the built-ins use 0) and `IsCandidate(Url)`, which judges the name or path only, because it runs on the key path and in `CanExecute`. Register an implementation in the container (`iTerminalApplicationBuilderExtension`) and it is picked up with no other wiring, or put it in the plug-in folder for the plug-in manager; `FileViewerRegistry` and `FileEditorRegistry` order them by priority, keeping registration order within a priority.

- **F3** (`ViewFileDialogCommand`): the first viewer that is a candidate and whose `CanOpenAsync` says yes gets `OpenAsync`. `CanOpenAsync` may read the file's header, so it runs on a worker thread. When no plug-in takes the file, the built-in chain runs as before, ending in the text viewer. A plug-in makes F3 available for a file the built-in viewers refuse (`CanExecute`).
- **F4** (`EditTextFileDialogCommand`): the "Edit with…" association still comes first. Then the first editor candidate, then the built-in text editor.
- **Dialogs go through `IFileOpenHost`**, never through a control: `ShowDialogAsync` shows the dialog on the screen that asked, and `ShowTextViewerAsync` is the fallback for a handler that only finds out after it has started (as the certificate viewer does).
- The built-in viewers are still the hand-written chain in `ViewFileDialogCommand`, not plug-ins. Moving them behind `IFileViewer` is the follow-up; the registry and the dispatch are already in place for it.
- **Loaded from a folder (Tools > Plugin manager).** `PluginManagerService` reads every `.dll` in `<DataDirectory>/plugins` (`PluginLocation`). A plug-in is an assembly with public classes that implement `IFileViewer` or `IFileEditor`; each class is created by the container and added to its registry, and unloading removes it again. Each file loads into its own collectible `AssemblyLoadContext` (`PluginLoadContext`): assemblies the application already has (iConsole, iTerminal.Core, the framework) are served by the default context, and anything else the plug-in needs is read from the same folder as bytes, so the file is not locked.
- **The dialog** (`PluginManagerDialog`, a `CheckList`) shows one row per file: a checked row is loaded. Space or Enter toggles it; the load or unload runs on a background task, and the row shows "loading" or "unloading" until it lands. R rescans the folder. A file with no viewer or editor in it, or one that does not load, is shown as failed with the reason.
- **Start-up.** The plug-ins the user loaded are saved in the `LoadedPlugins` setting (a JSON array of file names) and loaded again by `PluginStartupHostedService`, which notifies once if some fail. A failed plug-in stays saved until the user unloads it.
- **Unloading** removes the handlers at once; the assembly's memory goes when the garbage collector finds nothing referring to it, so a viewer dialog the plug-in opened keeps it until that dialog closes.
- **Tests:** `FileHandlerRegistryTest` and `FileHandlerRegistryChangesTest` (ordering, candidates, the host, add and remove), `FilePluginDispatchTest` (F3 and F4 offering, fall-through, `CanExecute`), `PluginManagerServiceTest` (scan, load and unload of a real plug-in copied from the test assembly, saved list, failures) and `PluginManagerDialogTest`.

### The voice recorder (`VoiceRecorderDialog`, no hot key, `Services/Voice/`)
Tools > Office or the command palette opens the recorder, which records from the default microphone (`IMicrophoneCaptureFactory`, the same recorder as voice input and evidence voice entries) to `Documents/iTerminal recordings/voice-yyyyMMdd-HHmmss.wav`. Enter starts and stops; a recording stops by itself at 60 seconds; Esc discards a recording that is going. **T** switches the AI transcription option: when the Whisper model is installed, the stopped recording is transcribed offline with `ISpeechRecognizer` and the text is saved beside the WAV file as a `.txt`. Transcription is off by default and nothing leaves the machine.

- The WAV file is written before the model runs, so a failed or cancelled transcription keeps the recording; the problem is shown in the dialog.
- The microphone, the disk and the model all run on background tasks; the dialog draws only its snapshot. `VoiceRecorderService.Discard` releases the device in the background, and only `Dispose` waits.
- The dialog's height is fixed when it opens, so it reserves room for a full transcript rather than the rows present at that moment.
- Not in the menus yet: the chord is the only entry point.
