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

### The contact manager (`ContactManagerDialog`, Ctrl+Shift+K, `Model/Contacts/`)
Tools > Contacts: name, email, phone, company and notes for the people the person keeps in touch with. N or Insert adds a contact and E, F2 or Enter changes the highlighted one, a field at a time (Enter or Tab for the next, Shift+Tab back, Enter on the last saves, Esc discards); the highlighted contact's fields are shown under the list. D or Delete asks y/n in the dialog. `/` filters the list as it is typed: every word must appear in some field, Enter keeps the filter, Esc clears it (and the next Esc closes the dialog).

- **Storage is the `Contacts` setting**, one JSON list read by `ContactStore` (same approach as the task list and the calendar): a table of its own would change the schema stamp and drop every cached table at start-up. A damaged value or a `null` entry is skipped, and the next save replaces it.
- **`Contact.Validate` is the one rule set:** a name is required, an email needs an `@` with text on both sides, a phone number needs a digit and only `+ - ( ) . /` and spaces. A refused save keeps the form open and moves the cursor to the field at fault.
- **The highlight follows the contact by id**, so it survives a re-sort or a filter; a contact saved under a filter that would hide it clears the filter.
- Also reachable from the `ui` AI tool as `contacts`.

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

- **Expand and collapse** are the `TreeView`'s own keys (Enter, Space, `+`, `-`, Left, Right); `*` expands every node.
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
