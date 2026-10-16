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
a folder — Favourites, Drives, History, Places, Folder Tree, SSH, Path. Whichever page is up, the
keys are the same: the arrows move, **Enter goes**, Esc cancels, F1 explains.

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
