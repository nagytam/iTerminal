### Audio playback (`iTerminal.Core/Audio/`, `Services/Music/`)
The music player owns its own audio path rather than driving an external player, which is what makes
real pause/seek/volume and a working output-device selector possible on both platforms.

- **Decoders** (`Audio/Decoders/`) normalise everything to signed 16-bit LE PCM (`PcmFormat`): `WaveAudioDecoder` (RIFF, 8/16/24/32-bit + float), `Mp3AudioDecoder` (managed, via the `NLayer` package), and `FfmpegAudioDecoder` which pipes `ffmpeg -f s16le -` for every extension in `AudioDecoderFactory.FfmpegExtensions` (the containers and codecs, the raw and telephony PCM formats, the game-audio streams and the tracker modules). ffmpeg detects the format from the content, so the extension only decides whether the viewer and the library take the file. `TestFiles` holds one short sample per format this ffmpeg could write; `FfmpegAudioFormatsTest` decodes each. `AudioDecoderFactory.IsSupported` distinguishes formats that always work from those needing ffmpeg on PATH.
- **Sinks** (`Audio/Sinks/`) consume that PCM: `WinMmAudioSink` (waveOut P/Invoke, device by index), `PulseAudioSink` (`libpulse-simple` P/Invoke, device by sink name), and `AplayAudioSink` (pipes into `aplay`) as the ALSA fallback. Each is wrapped by an `IAudioOutputBackend`; `AudioDeviceService` probes them **at runtime** by `Priority` — the same Linux binary may find PulseAudio on one machine and only ALSA on the next, so this cannot be a registration-time choice like `IPackageManagerService`.
- **`MusicPlayerService`** runs one background pump thread — at `ThreadPriority.AboveNormal`, because it is the only work in the app with a real-time deadline: the device drains its buffer whether or not the thread is scheduled, and being late is an audible gap rather than a slow repaint. It costs the render and task threads nothing, since the pump spends nearly all its life blocked in the sink's write. It owns the decoder and sink. The sink's blocking `Write` *is* the playback clock, so there is no timer; public transport methods only record a request and signal the pump, which keeps key handlers inside the 200 ms budget. Interrupting a write that is waiting on the device is what `IAudioSink.Stop` is for — the one sink member callable from another thread. The sink to stop is read *before* the request is posted (`PlayIndex`, `SetDevice`): read after, it could already be the sink the pump just opened for that request, and stopping it ended the new track after one chunk with "The audio device stopped accepting data". Track advancement happens inside the pump, so a playlist keeps playing after the panel navigates away — and the pump raises `StateChanged` as soon as it sets `Playlist.CurrentIndex`, since `SetState(Playing)` is a no-op from one track to the next and would otherwise leave the playlist panel highlighting the track that finished.
- **Saved playlists and the playing queue are stored differently, on purpose.** A `.m3u8` is a *document* the user made: plain M3U/M3U8 files via `IPlaylistService`, so they interoperate with other players and can be browsed, copied and opened in the panels. The *queue* is session state — no more a document than a panel's last folder — so it lives in SQLite (`MusicPlaylist` table, entity `MusicPlaylistEntry`, keyed by `Position` because a queue is an ordered list) behind `IMusicPlaylistStore`. Rows carry the tags alongside the path, so a restored queue draws itself without re-reading a single tag. `MusicSessionJob` writes session changes back, polling a version stamp (`PollInterval`, 5 s) because the player has no session-changed event and should not grow one — the pump would be raising it from inside playback. The queue used to be an auto-saved `last.m3u8`; the restore still reads that file once, when the table is empty, so an upgrade does not throw the queue away.
- **Restoring the session is `IMusicSessionRestoreService`, not the job**, because two things need it and neither can wait for the other: the job (so the queue comes back whether or not a music panel is ever opened) and `ContentModelMusicPlaylist`'s constructor (so a panel restored from the last session draws the saved queue on its *first* paint — panels are built while the app is still coming up, before the jobs have necessarily started, and waiting to be handed the queue is what made the panel show an empty list first and the queue a moment later). `RestoreOnce` is idempotent: whoever gets there first does the work. It is also exactly once *per run* — after the table has been read, memory is the truth, or clearing the queue and reopening the panel before the next save would hand the old queue straight back.
- **The cued track is restored too, stopped.** `Playlist.CurrentIndex` is saved as the `MusicCurrentIndex` setting rather than in the queue table: it is a cursor into that list, not a property of any row, and it moves every time a track ends while the queue's own version stamp never changes — so `MusicSessionJob` watches it in the settings signature, alongside the volume, not with the queue. It comes back cued and not playing: a restored session shows where the user was, it does not start making noise at start-up. An index that no longer fits the queue is dropped by `Playlist` itself.
- **A queue replaced from outside playback goes through `IMusicPlayerService.ReplacePlaylist`.** `Playlist` is a plain data type with no events of its own — every other change to it comes from the pump, which raises `StateChanged` afterwards anyway — so a restore that writes into the list directly leaves every panel drawing the list it was built with until some unrelated transport event repaints it.
- **The music library** (`IMusicLibraryService` → `MusicLibraryService`) is the opposite choice, and deliberately so: the *index* is a SQLite table (`MusicLibrary`, entity `MusicLibraryEntry`) because search must not re-read tags. `MusicLibraryIndexJob` (Background) calls `Preload()` immediately — reading the cached table is what makes the catalog panel usable, and leaving it to the lazy load would put a full SQLite read on whichever key handler opened the panel first — then walks the music folder five seconds after start-up and every 15 minutes after that, re-reading tags only for files whose size or timestamp moved; the steady-state cost is a directory enumeration. Durations are probed only for WAV/MP3, by constructing the managed decoders directly rather than through `AudioDecoderFactory`, so a scan can never fork one ffmpeg per file.
- **The table is what the catalog panel draws, not the scan.** `EnsureLoaded` fills the in-memory copy from `MusicLibrary` before anything else touches it, so a panel opened in a session that has not scanned yet still lists the previous run's index. (This only became visible once the database stopped being recreated at every start-up — see the schema-stamp note above; until then the table was empty at every launch and the catalog could not fill until a scan finished, however early it read.) That load is marked done only when it *succeeds* — a database being recreated at start-up or held by a second instance would otherwise leave the catalog empty for the whole session, with nothing left to trigger another look — and a failed attempt is retried no more often than `LoadRetryInterval` (5 s), because the panel reads this on every repaint.
- **A scan publishes as it goes, in both directions.** Every `ScanFlushBatchSize` (250) freshly read files, or every `ScanFlushInterval` (2 s), `FlushScan` writes that batch to the table *and* folds it into the in-memory snapshot. So a first scan of a large folder fills the panel while it runs (the panel rebuilds from `Entries` on each progress report), and an app closed mid-scan keeps what had been indexed. Deletions are the exception: a row is only dropped once the whole walk is over, since until then "not seen yet" and "gone" look the same — which is also why the running snapshot is seeded from the cached library rather than from what the scan has read, and can therefore never shrink under a user who is looking at it.
- **A scan runs in two reported stages**, because "indexed 120 files" means nothing without a denominator: *discovery* (the recursive folder walk, which can only report a rising count) then *indexing* (the per-file pass, which knows the total). `MusicIndexingProgress` is the immutable snapshot of that — `Phase`, `DiscoveredFiles`, `IndexedFiles`, and `IsDiscoveryComplete`, which is true exactly from the indexing stage on. `DiscoveredFiles` counts *audio* files, never files: a real music folder is mostly artwork cache (a stock `~/Music` with an iTunes library is ~2 700 `.itc2` files around ~30 tracks), and a denominator counting those could never be reached — the UI says "audio files" for that reason. Discovery enumerates with `IgnoreInaccessible = true` and guards the file listing, the directory listing and each sub-folder separately, so one unreadable entry costs that folder and not the sub-tree under it. `IsIndexing` is derived from it rather than kept alongside it. Progress arrives on `IndexingProgressChanged` (rate-limited to `ProgressReportInterval`, 250 ms, with stage changes and the end of the scan always published) — `LibraryChanged` only fires once the whole scan has landed, so the progress event is the one a panel needs to repaint while work is still running. The catalog panel puts it in the two places that frame the listing and must agree: the footer, and the panel title — where `GetHeader` reserves the state on the right and shrinks only the path, because `Shrink` elides a line's *middle*, which is exactly where the counts would be. It is deliberately *not* a row: the title and footer are on screen for every listing, so a progress row would spend a line of the catalog repeating them.
- The **music folder** comes from the `MusicFolder` setting (`SettingsMusicTabPage`, tab 4 of the settings dialog), defaulting to `Environment.SpecialFolder.MyMusic`. The job polls that setting rather than subscribing to it — `ISettingsService` is a plain key/value store with no change notification — so changing the folder in Settings is enough to trigger a re-index.
- Search runs against an in-memory copy of the table, so the catalog panel calls `Search` straight from the key handler.
- **Tag reading** has two shapes off one parser. `MusicTagReader.Read` returns title/artist/album for the playlist and the library index; `MusicTagReader.ReadAll` returns the whole ID3 block (every text frame, comments, cover-art size, numeric genres resolved) for the F3 viewer. `FlacTagReader` covers what ID3 cannot: FLAC's Vorbis comments *and* its STREAMINFO, which is also where a FLAC's duration and bit depth come from without forking ffmpeg. Ogg/Opus/M4A tags are not read — those files show their name, size and "needs ffmpeg" instead of inventing metadata.
- **`IMusicMetadataService`** (→ `MusicMetadataService`) is what the viewer actually calls: it picks the tag reader by container and probes duration/format with the managed decoders only, for the same reason `MusicLibraryService.ProbeDuration` does. Async by contract; the dialog awaits it on a background task and repaints when it lands.

### What the F3 viewer reads (`iConsole/Services/`)
F3 on a file that is not music, video, a picture or a solution opens `FileViewerDialog`, which asks
`FileToTextConverterService` for lines. Each format has a reader, and every reader takes the file's bytes
rather than a path, so a document on an SSH, archive or memory drive reads the same as one on disk:

- **OpenXML and OpenDocument** (`OfficeTextRenderer`): Word, Excel, PowerPoint, and `.odt`, `.ods`, `.odp`/`.otp`.
- **Office 97–2003** (`LegacyOfficeTextRenderer`): `.doc`, `.xls`, `.ppt`. These are OLE2 compound files, opened
  with OpenMcdf. The reader takes the text streams only: the piece table and text stream of a `.doc`, the BIFF8
  cell records and shared-string table of an `.xls` (dates and stored errors shown as they are in the sheet), and
  the text atoms of a `.ppt` reached through its persist directory, with the notes list for speaker notes.
  A password-protected workbook says so.
- **RTF** (`RtfTextReader`): a hand-written reader. Destinations that are not body text (fonts, colours, styles,
  metadata, pictures, objects, headers and footers, list numbering) are skipped whole; `\'hh` is decoded with the
  declared `\ansicpg` and `\uN` skips its fallback.
- **EPUB** (`EpubTextReader`): `container.xml` names the OPF, the spine orders the XHTML chapters. The markup goes
  through `HtmlTextExtractor`, shared with the e-mail and Kindle readers.
- **OpenDocument drawings** (`.odg`, `.otg`): one section per page, as presentations are.
- **XPS and OpenXPS** (`XpsTextReader`, `.xps`, `.oxps`): the `FixedDocumentSequence` gives the documents, each
  `FixedDocument` its pages, and a page's `Glyphs` runs are grouped into lines by their origin.
- **E-mail** (`MimeMessageReader`, `.eml`): the headers unfolded and their RFC 2047 words decoded, the body from
  base64 or quoted-printable in its charset, the plain part preferred over HTML. Attachments are listed by name.
- **Outlook messages** (`OutlookMessageReader`, `.msg`): the MAPI property streams of the compound file, Unicode
  when present, and the attachment storages' names.
- **FictionBook 2** (`Fb2TextReader`, `.fb2`): sections as headings, paragraphs, verses, tables, notes bodies.
- **Kindle MOBI and AZW** (`MobiTextReader`, `.mobi`, `.azw`): the Palm database's PalmDOC text records, with the
  trailing entries the MOBI header declares stripped and the markup read by `HtmlTextExtractor`. Huffman-compressed
  (KF8/AZW3) books are reported, not read.
- **HTML, PDF, plain text, images as ASCII art and .NET binaries** are read as before.

These readers do not feed the document search index: `DocumentTextExtractorFactory` still lists only the
extensions it always had.

### Code pages in the viewer and the editor (`TextEncodings`)
**F8 / Shift+F8** in the F3 viewer and the F4 editor re-read the text under the next / previous code page (UTF-8, UTF-16, Windows-1250/1251/1252/1253/1254/1257, ISO-8859-1/2/15, DOS 437/852/866, KOI8-R, Shift-JIS, GBK, Korean, Big5); the status bar's encoding indicator shows the current one and the editor saves with it.

- **A file nobody chose a code page for is decoded by `TextEncodings.Decode`**: a BOM decides, else UTF-8 if the bytes are valid UTF-8, else the system's ANSI code page (Latin-1 where there is none). It used to be `Encoding.Default`, i.e. UTF-8 *with replacement characters*, which showed a Windows-1250 file as `�` and — worse — saved it back that way.
- **Switching is `TextEncodings.Reinterpret`, in memory**: the lines are turned back into the bytes the current encoding made of them and decoded under the new one. That is exact for what `Decode` yields (valid UTF-8 and single-byte pages round-trip), needs no second read of the file (any drive), and keeps the editor's unsaved edits; the editor counts the file as changed because saving now writes other bytes. Only text is switchable (`FileToTextConverterService.IsEncodingRelevant`): not a hex dump, PDF, Office document, image or binary.

### The F3 music file viewer
F3 on a local audio file opens `MusicFileViewerDialog` (`iTerminal.Core/Dialogs/Music/`): artist, title,
album and format at the top, every tag in a scrollable list, and a transport with a progress bar at
the bottom (Space play/pause, Enter from the start, `s` stop, ←/→ seek, +/- volume).

- It plays through the **shared `IMusicPlayerService`**, never a private decoder — otherwise viewing a
  track would start a second stream over whatever the music panels are playing. Starting a file that
  is not queued appends it to the playlist, the same thing choosing it in a panel does, and closing
  the viewer leaves it playing.
- The transport applies only while the player is loaded with *this* file (`CurrentTrack.Path` match),
  so seeking in a viewer opened over a different playing track cannot move that other track.
- The progress bar needs no timer: `Dialog.OnIdle` marks the dialog changed every idle pass, and
  `Draw` reads the player's live position.
- `ViewFileDialogCommand` routes to it only when the file exists locally (`File.Exists`), like the
  solution viewer — the player decodes from a path, so an `ssh://` track has nothing to open.

### The music player is two panel drives, not a dialog
Ctrl+F7 (or Ctrl+Shift+M) runs `MusicPanelsCommand`, which points the left panel at `musiccatalog://`
and the right panel at `musicplaylist://`. There is no music dialog: the library and the queue are
drives, so "add this album" is the same F5 that copies a file between any other two panels.

- **`ContentModelMusicPlaylist`** lists the queue, preceded by three control folders — `[Transport]`,
  `[Volume]` and `[Output]` — reachable with the cursor and `Enter`. Every control the old dialog
  bound to a key is a row in one of them, which is what makes the drive usable without memorising
  anything. The header carries now-playing plus a progress bar and the footer the volume, repeat,
  shuffle and output device. `Delete` drops a track from the queue (`F8`, "delete the file", stays
  what it always was and is not bound here). `Alt+<text>` filters it, the same type-to-search the
  file panels use: the model overrides the `Filter` setter and rebuilds the listing with only the
  matching tracks — artist, album, title or file name, every word having to match, in any order —
  rather than dimming the rest, because the row wanted among a few hundred tracks is as likely to be
  off-screen as on it. Rows keep their queue position (`PlaylistIndex`, and the number they show), so
  `Enter` and `Delete` still act on the real track; the three control folders stay listed, since they
  are how the panel is operated at all; `Esc` clears it.
- **`ContentModelMusicCatalog`** lists artists at the root, albums one level down, tracks below that.
  Search is the panel's own type-to-search: `Alt+<text>` sets `Filter`, and the model overrides the
  setter to replace the listing with library-wide hits rather than dimming non-matching rows — the
  point of searching a catalog is to reach rows that are not in the current folder. `Esc` clears it.
  The listing is nothing but the folder's own rows: the indexing state lives in the header and the
  footer, which are drawn for every listing anyway. `Ctrl+R` (`Refresh(forceRefresh: true)`) starts a
  re-index on a background task — the footer names that key, since no row does — and the panel
  repaints from `LibraryChanged`.
- **Marking is the catalog's only stateful UI.** `Insert` (`ToggleMusicSelectionCommand`) marks the
  row and steps down, F5 then adds every marked row instead of the cursor row, and the operation
  consumes the marks. The marks live in the *model* as a set of keys (`track <path>` /
  `Artist|Album <artist> <album>`), never on the `MusicEntry` rows: the listing is rebuilt from the
  library on every refresh — several times a second while a scan runs — so anything stored on a row
  would not survive a repaint. `MusicEntry.IsSelected` is projected onto each row as it is built.
  Rows that stand for no tracks (`..`) are stepped over rather than marked, and
  navigating or searching clears the marks, so F5 can never add rows the user cannot see.
- **Navigating clears the filter on both music drives** (`ContentModelMusicBase.SetUrl`). This is not
  cosmetic housekeeping the way it is for a file panel: both models build their *listing* from the
  filter, so one left set across a navigation would have the panel draw the old hits under the new URL.
- Both models derive from `ContentModelMusicBase`, are read-only, refresh synchronously (a playlist
  and an in-memory index cannot approach the 200 ms budget), and are `IDisposable` — they subscribe
  to singletons that outlive them, so `ContentModelService.AddContentModel` disposes the model it
  replaces. That is the only reason content models have a lifecycle at all.
- Artist and album URL segments are percent-escaped (`Uri.EscapeDataString`). This is not cosmetic:
  without it "AC/DC" would parse as an album inside an artist called "AC".
- The commands live in `iTerminal.Core/Commands/Music/` and are attached to *every* `ContentBrowser`,
  ahead of the file-system ones. `MusicPanelCommand.FocusedMusicModel` gates them: the focused model
  must be a music model **and** be this browser's own model. Both halves matter — a control-tree walk
  offers a key to the focused control first but then to every control, so without the identity check
  a key the focused panel declined would be picked up by the other panel.

### Document management (`iTerminal.Core/Documents/`, `Services/Documents/`, `Dialogs/Documents/`)
Alt+F3 (`DocumentManagerDialogCommand`) opens `DocumentManagerDialog`: a search box, the results, and
an optional preview. Behind it, `DocumentLibraryIndexJob` walks the configured documents folder,
extracts text from every file an extractor understands, and stores both the metadata and the text.

- **Three stores, not one, and the split is the design.** `DocumentEntry` (table `Documents`) is the
  metadata and is mirrored in memory — it is what lists, counts and sorts. `DocumentContentEntry`
  (table `DocumentContents`) is the extracted text, read one row at a time for the preview and never
  held in memory. `DocumentSearchIndex` is a SQLite **FTS5 virtual table** (`DocumentSearch`) and is
  what search actually runs against. The music library keeps everything in memory and searches there;
  that cannot work here, because a music tag is tens of bytes and a document's text is tens of
  kilobytes — the same design over a real documents folder would hold hundreds of megabytes of prose
  in the heap to answer a query SQLite answers from an index. **Consequently `SearchAsync` is
  asynchronous** where `IMusicLibraryService.Search` is not: it hits the database, so the dialog runs
  it on a background task and never from a key handler.
- **The FTS table is invisible to EF Core**, which is why `DocumentSearchIndex` exists as a class
  rather than three methods on the service. The app has no migrations — a model change drops and
  recreates the database — and `EnsureCreated` knows nothing about a virtual table, so every entry
  point calls `EnsureCreated` first, and it rebuilds the index from `DocumentContents` when the
  metadata survived something the index did not. Without that, search would come back empty until the
  next full rescan re-extracted every PDF, which is minutes of work to rebuild something derived from
  what is already stored. There is a LIKE fallback for a SQLite built without FTS5.
- **Name and metadata hits are matched in memory and always sort ahead of content hits.** Someone who
  types a file name wants that file, not the twenty documents that mention it. Only the body search
  goes to the database.
- **Extractors** (`Documents/Extractors/`) implement `IDocumentTextExtractor` and are chosen by
  extension through `DocumentTextExtractorFactory`. `PlainTextExtractor` (sniffs UTF-8, falls back to
  Latin-1 — never to UTF-8 with replacement characters, since a token containing U+FFFD is one no
  query can match), `MarkupTextExtractor` (a scanner, not a parser: the input is whatever is in the
  folder, and a parser would reject exactly the half-saved files most in need of being searchable),
  `OfficeTextExtractor` (OOXML and OpenDocument are zips of XML — no interop, no package), and
  `PdfTextExtractor`. Extraction is deliberately **synchronous**: it is CPU and disk work with nothing
  to await, and what keeps it off the render thread is that only the indexing job calls it.
- **`PdfTextExtractor` is hand-written for the same reason the ID3 and FLAC readers are.** It scans
  for `stream … endstream` pairs rather than following the xref, which reads damaged and
  incrementally-updated files a strict walk rejects; it inflates Flate streams (with the PNG
  predictor, which object streams use); and it reads the strings the text operators draw. The
  declared `/Length` is only ever a hint, verified against an `endstream` — producers get it wrong.
  A large negative `TJ` adjustment becomes a space, which is how most producers write one. What it
  *cannot* do is map a subset CID font's glyph indices back to characters; rather than index the
  mojibake, `LooksLikeText` checks the result and the document is recorded `Empty` with a note. The
  same note covers scanned PDFs, whose answer is OCR.
- **Images contribute text from two places, kept separate on purpose.** `ImageMetadataReader` reads
  PNG text chunks, JPEG comments, EXIF descriptions and XMP straight from the header bytes — free,
  always works, and for a photograph usually the only text worth finding. OCR is the other, and runs
  only when an `IOcrEngine` reports itself installed. `TesseractOcrEngine` shells out to `tesseract`,
  the same bargain the audio decoders strike with ffmpeg, and probes availability **once** — a scan
  asks per file. It is registered behind `SettingsGatedOcrEngine`, so the extractor only ever asks
  "can this run", never "does the user want it to"; the engine's `DisplayName` distinguishes "off"
  from "not installed", because only one of those is fixed in the settings dialog.
- **Extraction status is stored, not re-derived.** A scanned PDF, a file no extractor handles and a
  file that threw all yield an empty string, and an index that recorded them identically would be
  lying. `DocumentExtractionStatus` plus `ExtractionNote` is what lets the preview pane say *why*
  there is nothing to show instead of drawing a blank the user reads as "still loading".
- **A rescan re-extracts only what moved** (size or last-write time), which matters far more than it
  does for music: re-reading every tag in a music folder is a second, re-extracting every PDF and
  re-OCR'ing every scan is minutes — and the job would do it on every pass. That is also why
  `DocumentLibraryIndexJob`'s intervals are much longer than `MusicLibraryIndexJob`'s (20 s start-up
  delay, 1 hour rescan), and why the job polls the folder setting rather than subscribing to it
  (`ISettingsService` has no change notification).
- The scan runs in the same two reported stages as the music one (`DocumentIndexingProgress`:
  `Discovering` → `Indexing`), flushes in batches so the dialog fills while a first scan runs, and
  only drops rows for missing files once the whole walk is over. Unlike the music scan, discovery
  counts **every** file rather than only supported ones: an unsupported file is still listed and
  still searchable by name, so hiding it from the denominator would make the count disagree with the
  folder. Progress carries the current file name — the counts alone cannot show which document is
  taking the seconds. Build output, VCS internals, dot-files and Office `~$` lock files are skipped.
- The **documents folder** comes from the `DocumentsFolder` setting (`SettingsDocumentsTabPage`, tab 5
  of the settings dialog), defaulting to `\users\tamas\nata\documents` — anchored to the system drive
  on Windows by `DocumentLibraryService.ResolveDocumentsFolder`, because a path that starts with a
  separator but names no drive resolves against whichever drive the process happens to be running on.

### The F3 video and image viewers (`iTerminal.Core/Video/`, `Dialogs/Video/`)
F3 on a video (`VideoFileTypes`) opens `VideoFileViewerDialog`; F3 on a picture (`ImageFileTypes`) opens `ImageFileViewerDialog`. Both draw the picture as ASCII art, both need a real local file and ffmpeg/ffprobe on PATH, and `ViewFileDialogCommand` routes video ahead of audio because `.mp4` and `.webm` are in the audio list too.

- **ffmpeg does the scaling.** `FfmpegVideoDecoder` pipes `-f rawvideo -pix_fmt rgb24` after a `scale=<columns>:<rows>:flags=area,fps=<rate>` filter, so a frame is exactly the character grid's size (a few kilobytes) and the app never touches full-resolution pixels. The rate is capped at `MaxFrameRate` (24). Seeking and resizing restart the process with `-ss`, like `FfmpegAudioDecoder`. To ffmpeg a picture is a one-frame video, which is why the image viewer reuses the decoder (an animated GIF shows its first frame).
- **`AsciiArt` turns pixels into characters.** The picture is carried by the character — a 70-step luma ramp, dense = bright — because the console only has 16 colours. `Mono` uses white/grey; `Color` picks one of the 16 console colours from which channels are strong (`ColorOf`). `Fit` keeps the picture's shape on cells that are about twice as tall as wide.
- **`VideoPlayer` is one thread and a clock, and the UI never waits on it.** Transport methods record a request and wake the thread; `Draw` reads the latest immutable `VideoFrame`. The clock is the player's own stopwatch (the viewer is silent), and it is **re-based on the first frame after every reopen**, because starting ffmpeg takes a moment that must not be replayed as a burst of late frames. After a reopen a paused or stopped player reads exactly one frame so the picture follows a seek. A stream that never delivers its first frame is reported as an error and is *not* reopened (end-of-stream otherwise goes back to a stopped first picture, which would loop on a broken file).
- **Resizing is driven from `Draw`**: it fits the picture to the room and calls `Resize`, which is a no-op when nothing changed. The image dialog does the same by starting a background load for the newest size only.
- **The transport buttons are mouse-only** (`CanFocus = false`) with the keys bound on the dialog (Space, ←/→, Shift+←/→, Home, `s`, `c`); a focusable button would make Space mean "press the focused button". Clicking the progress bar seeks.
- Tests use `FakeVideoDecoder`, so they need no ffmpeg; the real decoder's output parsing (`ParseProbe`) is tested separately.

### Viewer navigation, image metadata and audio tags
- **Wrap (F2)**: `TextViewerControl.WrapLines` breaks long lines to the view's width. Its vertical scroll counts screen
  rows (`ScreenRows`), the cursor stays on its line, and a line's rows are highlighted together. Only the viewer wraps; the
  editor keeps lines whole. Hex mode has no wrap.
- **Go to line (F5)**: `GoToLineDialog` takes a number and calls `TextViewerControl.ShowLine`, which puts the line a third
  of the way down the view. A number past the end goes to the last line.
- **Search hits**: F3 and Ctrl+F3 step through the hits and reveal them through `ShowLine`, so a wrapped hit is shown on its
  own row. The status bar's counter reads "Hit n/N": n is the hit the cursor is on, or the last one before it.
- **Image metadata (`i` in the image viewer)**: `ImageInfoReader` reads a 1 MB prefix of a PNG, JPEG, GIF, BMP, WebP or
  TIFF file: its format and dimensions, PNG text chunks and JPEG comments, and the EXIF fields (make, model, lens,
  exposure, aperture, focal length, ISO, date taken, GPS position and altitude). Nothing is decoded; the dialog scrolls the
  list with the arrow and page keys.
- **Image text (`e` in the metadata view)**: `ImageMetadataEditorDialog` edits the description, artist, copyright and comment
  of a JPEG or PNG, prefilled from `ImageMetadataEdit.From`, which maps the EXIF tags and the PNG keywords (Author is the
  artist). `ImageMetadataWriter` rebuilds the file in memory: in a JPEG it rewrites the three EXIF text tags of the first
  directory and the COM comment, and re-serialises the rest of the EXIF block (camera, exposure, GPS) with its offsets moved;
  in a PNG it drops the tEXt/iTXt chunks with those keywords and writes new ones before the first IDAT, as tEXt for Latin-1
  text and as iTXt in UTF-8 otherwise, with the CRC-32 computed. The picture data is copied byte for byte. EXIF and comment
  text are UTF-8. Other formats are refused. The file is replaced through `FileReplacement`, and the metadata is read again
  after a save.
- **Strip Metadata (Edit menu)** runs `ImageMetadataWriter.Strip` over the marked pictures in one run; see file-operations.md.
- **Audio tags (`e` in the music viewer)**: `MusicTagEditorDialog` edits title, artist, album, year, track, genre and
  comment of an MP3 or FLAC. `AudioTagWriter` rewrites only the tag block: in an MP3 the ID3v2 block (other frames such as
  cover art, and comments with a description, are kept, and the audio is copied byte for byte); in a FLAC the Vorbis comment
  block (other comments and metadata blocks are kept). The file is written beside the original and moved over it, so a
  failed save leaves the original intact. ID3v2.2 tags and other formats are not written.
