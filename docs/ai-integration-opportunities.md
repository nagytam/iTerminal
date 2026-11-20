# AI integration opportunities

A survey of AI features that could be added to iTerminal, what each would take given what is
already in the code, and the order in which it would be worth building them. It is a research
document: nothing here is scheduled, and each candidate is a proposal for its own
`/incremental-improvement` run.

**How it was checked.** The AI code in `iTerminal.Core/AI/`, `iTerminal.Core/Speech/`,
`iTerminal.Core/Services/Translation/`, `iTerminal.Core/Services/Documents/` and the
architecture docs (`panels-and-drives.md`, `dialogs.md`, `media-and-documents.md`,
`runtime-dependencies.md`, `key-patterns.md`) was read. External facts (protocol and product
releases) come from a web search done while writing and are marked as such; they should be
confirmed against the primary sources before a feature depends on them. Nothing was built or run.

## 1. What already exists

| Area | What is there today | Where |
| --- | --- | --- |
| Chat over any model | One `IChatClient` built from `AI:Provider`: `None`, `Ollama` (default, downloaded on demand) or `OpenAI` (any OpenAI-compatible endpoint, key from env var or password manager). | `AiChatClientFactory`, `AiChatService`, `AiOptions` |
| Prompt bar | The AI bar (Ctrl+F2 details, Ctrl+B swaps for the terminal bar), one row when the model is unreachable. | `AiPromptBar`, `Content` |
| Tool calling over the app | About 60 `AITool`s: navigation, panel state, cursor, marks, sort, filter, refresh, copy, move, rename, create folder, delete (recycle bin), compress, extract, compare and sync folders, open dialogs (`ui` tool), quit. | `AiPanelTools*.cs`, `AiChatService.cs` |
| Approval for writes | Every file-writing tool previews first; `confirmed=true` only *asks* the app's approval dialog. Only the user's Y authorises. | `AiActionApprovalService`, `AiActionApprovalDialog` |
| File content to the model | `read_panel_file` sends a quick-view preview (64 KiB, 2,000 lines) after approval, with a warning that it may leave the machine. | `AiPanelTools.cs` |
| Meaning search over documents | Optional embeddings (`AI:EmbeddingModelId`) for the document index; the text of every indexed document is sent to the endpoint on each scan when it is on. | `media-and-documents.md`, `AiOptions` |
| LLM translation | `LlmTranslationProvider` uses the chat model; a LibreTranslate API is tried first in Automatic mode. | `Services/Translation/` |
| Voice input | Alt+F1 records, Whisper (`ggml-base.bin`, pinned and hash-checked) transcribes offline into the question. | `Speech/`, `runtime-dependencies.md` |
| Offline speech and local models | Ollama and the Whisper model are downloaded on demand; a system Ollama is used as it is. | `OllamaServer`, `OllamaRuntimeDependency`, `WhisperRuntimeDependency` |
| Non-AI safety building blocks | Hash reputation (`HashReputation`), checksums and signing, Recycle Bin with restore, folder sync plans, a read-only SQLite viewer with three write guards. | `Services/Reputation`, `Services/Checksums`, `Services/Signing`, `dialogs.md` |

**Gaps that shape the candidates below.** The model can act on the panels but cannot see the
terminal output, the selected content of a non-file view (git diff, docker logs, a mail body),
images, or anything outside the two panels. Its context is a chat history with no retrieval
across folders beyond the document index. There is no MCP client or server, no structured
"explain this" entry point from other views, and no per-feature provider routing (one model
serves everything).

## 2. Candidate features

Each entry gives: what it does, what it reuses, what is missing, the safety and threading
concerns, and a rough size (**S** under a week of work, **M** one to three weeks, **L** more).
Sizes are estimates for one developer who knows the code.

### 2.1 Panel and file management

**A. Smart rename with preview (M).** "Rename the photos by date taken, lowercase, no spaces."
The model proposes a rename plan (old name to new name) and the app shows it as a table; the
existing `rename_entry` and `RenameUndoService` do the work after Y.
- *Reuses:* `rename_entry`, `RenameUndoService`, the approval dialog's plan view, the EXIF reader.
- *Missing:* a bulk plan tool (`plan_renames`) that returns the whole mapping, and a dry-run
  conflict check (two targets with the same name, names that are illegal on the target drive).
- *Concerns:* the plan must be generated from a snapshot of the marked rows, so a listing that
  changes mid-plan is refused (`IsCurrentUrl()` rule). Conflicts are reported, never resolved by the model.

**B. Organise by content (L).** "Move every invoice from 2025 into a folder per supplier."
Read the first part of each file, classify, and propose a move plan that the approval dialog shows.
- *Reuses:* `read_panel_file` (approval per file would be too slow; a batch approval is needed),
  `move_to_other_panel`, the sync plan view.
- *Missing:* a batch read approval, a classifier that runs on a background task with progress, and a
  cap on how many files one plan may touch.
- *Concerns:* file content is untrusted. A PDF can say "move everything to /tmp". The plan is shown
  to the user and the model cannot approve it, which already covers this, but the prompt must say
  file content is data.

**C. Explain a file or folder (S).** Ask about the entry under the cursor ("what is this log about?").
Built on `read_panel_file` plus the document extractors (`media-and-documents.md`) for PDFs,
Office files and archives (list the entries, do not extract).
- *Missing:* archive and binary handling in the reader (today it reads text previews only).
- *Concerns:* the read approval already warns that content leaves the machine. For a remote
  provider this should say so in the answer's first line.

**D. Duplicate and clutter review (M).** Explain why the duplicate finder matched two files, or
suggest which copy to keep. The model sees only the summary the duplicate finder already holds
(size, hash, dates, path), so no file content needs approval.
- *Reuses:* `Services/Duplicates`, `DiskUsage`.
- *Concerns:* deletion stays the existing `delete_entries` flow with recycle bin and approval.

### 2.2 Search

**E. Natural-language search over the whole machine (M).** "Find the contract I signed with the
bank last spring." Turn the request into a `search` drive query (name, date, size, type, content
match) and show the results in a panel. The `search` drive already exists (`panels-and-drives.md`).
- *Missing:* a tool that builds a search from parameters, and an answer that can say "no match".
- *Concerns:* the search runs on a task, never in the key handler; results land through
  `ReplaceEntriesAndRestoreFocus`.

**F. Meaning search on file names and small text files (M).** Extend the embedding index beyond
the documents folder to chosen folders, so "tax" finds `Steuererklärung 2025.pdf`. Requires
`AI:EmbeddingModelId`, a local endpoint for privacy, and an explicit folder list.
- *Reuses:* the document index's embedding pipeline, `AiChatClientFactory.CreateEmbeddingGenerator`.
- *Concerns:* privacy. The current doc warns that embeddings send every document's text to the
  endpoint. The same warning and default (off, local-only recommended) must apply to any new folder.
  Index size grows with the folder list, so a size cap and a rebuild command are needed.

**G. Ask about the content of a search hit (S).** After E, let the model read a hit with `read_panel_file`.
Same approval as C.

### 2.3 Terminal and command assistance

**H. Explain and fix a failed command (M).** When the terminal bar or a command run returns a
non-zero exit, offer "Explain" in the AI bar with the last output (bounded to a few KiB, with
secrets redacted; see 3.2). The answer may propose a command.
- *Reuses:* `TerminalModel.StartCommand`, the output buffer, the AI bar.
- *Missing:* a way to pass the terminal output to the model without the user copying it; redaction.
- *Concerns:* a proposed command is only ever typed into the command line, never run. This is the
  same rule as `RevealCommandLine`: the user presses Enter.

**I. Generate a command from a sentence (S).** "Show the 10 largest files under this folder" gives
a command in the command line, not run. The model is told the shell (`cmd`, PowerShell, bash).
- *Concerns:* the output is never run by the app. The command palette should show the risk for
  any command that deletes or formats, and the answer should mention it.

**J. Command-line history search by meaning (M).** Find the command from last week that did the
thing. Uses the existing folder/command history, no new data. Low risk, moderate value.

### 2.4 Developer tools

**K. Commit messages and diff explanation in the git client (S).** The git client already has the
diff. "Generate message" fills the commit box from the staged diff; "Explain" summarises a commit
(history drive). Bounded to the staged size; big diffs are summarised per file.
- *Reuses:* `IGitService`, the git client dialog, the history drive.
- *Concerns:* the diff is repository content and may hold secrets; the same redaction as 3.2 applies,
  and a private repository sent to a remote provider needs the same warning as the read approval.

**L. Build and test failure explanation (S).** After `dotnet build` or `dotnet test` in the solution
viewer, "Explain" sends the error lines (not the whole log) to the model and points at file and line.
- *Reuses:* `IDotnetSolutionService`, the diagnostics the solution viewer already parses.

**M. Docker and container log help (S).** "Why did this container exit?" The Docker drive has the
logs and inspect data; the model gets the last N lines and the exit code.
- *Concerns:* logs contain secrets from environment variables. Redact `KEY=`, `TOKEN=`, `PASSWORD=`
  patterns before sending.

**N. Solution and project questions (M).** "Which projects reference Core?" Answered from the
parsed solution model, not from file reads. Gives the model a structured tool rather than raw XML.

### 2.5 Media and documents

**O. Audio transcription of files (S).** Whisper already runs offline on recordings (Alt+F1).
Extending it to F3 on an audio file gives a transcript file next to it. Transcription is local, so
the privacy risk is the lowest in this list; the cost is CPU time, so it runs as a background task
with progress.
- *Reuses:* `WhisperRecognizer`, `SpeechToTextService` (one session at a time; a file job would need its own queue).

**P. Image description and alt text (M).** Needs a vision-capable model (Ollama has some; the OpenAI
provider works with GPT-class vision models). Generate a caption into the image's tag store or a
sidecar `.txt`, and make the caption searchable through E.
- *Missing:* image input in `AiChatService`; today the chat only sends text.
- *Concerns:* writing sidecar files is a file write, so it goes through the approval dialog or a
  batch preview. Images are sent to the endpoint like text, so the read approval is needed.

**Q. Music library helpers (S).** Suggest tags and a playlist from a description ("calm things for
the evening") using the existing music index (artist, album, genre, year). No file content is read.

**R. Subtitles and translation for video (M).** Whisper transcribes the audio track; the translation
service (LLM or LibreTranslate) writes a subtitle file. Reuses the translator. No subtitle format
writer exists yet (`MusicTagReader` is audio tags only), so a small SRT/VTT writer is new work.

### 2.6 Data files

**S. Natural-language query over CSV, Parquet and SQLite (M).** "Average amount by month for 2025."
The model writes a query, which runs through the existing read-only SQLite guards or a Parquet/CSV
reader, and the result appears as a table.
- *Reuses:* `SqliteStatementChecker` (only `SELECT`, `WITH`, `EXPLAIN`, `PRAGMA` from a fixed list),
  the read-only connection, the Parquet and CSV viewers, the HDF5 viewer.
- *Concerns:* a generated statement is untrusted input. It goes through the same checker and the
  read-only connection; the checker is the wall, not the model's promise. Results are capped in rows.
  Writes are never allowed.

**T. Chart from a question (S).** "Plot monthly totals." The result of S is drawn with the chart
controls the app already uses (`DiskUsageDialog`, `WeatherDialog`, `StockBrowserDialog`). Low effort
once S exists.

### 2.7 Mail, web and encyclopedia

**U. Summarise mail and draft replies (M).** The mail service already reads messages. A summary and a
draft reply go into the compose view; sending stays the user's act. Mail bodies are private: the
remote-provider warning is required, and a per-account opt-in should be the default.

**V. Summarise a web page (S).** The web browser and the encyclopedia already fetch pages. "Summarise"
on the current page; the page text is the content sent, so the same warning applies.

**W. Answer from the encyclopedia with citations (M).** Retrieval over the encyclopedia results with
the source link in each sentence. Relies on the existing encyclopedia lookup; citations must be
checked against the returned text, not generated.

### 2.8 Assistant platform

**X. MCP client (L).** Let the AI bar use tools from MCP servers (a user's filesystem server, a
calendar, a repository server), configured in Settings. The 2026-07-28 MCP specification (see
Sources) moves to a stateless core, deprecates Roots, Sampling and Logging, and deprecates the
HTTP+SSE transport in favour of Streamable HTTP. A client written to the stdio and Streamable HTTP
transports would target that revision and avoid the deprecated parts. The official C# SDK
(`ModelContextProtocol`, 1.0 in March 2026 by search results) integrates with
`Microsoft.Extensions.AI`, so tools would arrive as `AITool`s and need no new chat plumbing.
- *Reuses:* the approval service (every MCP tool that writes is treated as a write), `AiChatService` tool list.
- *Missing:* server configuration and trust (a server is code that runs on the machine), a
  per-server allowlist, and a way to show which server a tool came from in the approval dialog.
- *Concerns:* credentials come from the vault or password manager, never from a server's URL
  (`CLAUDE.md`). Each MCP server process must be killed on exit, like the recorders in
  `WinMmMicrophoneCapture` and `ProcessMicrophoneCapture`.

**Y. iTerminal as an MCP server (M).** Expose the panel tools to an external agent (a coding agent
in the terminal), so that agent can navigate and act on the file panels under the same approval.
- *Concerns:* this widens the set of callers. The approval dialog must still show every write, and the
  server must listen only on stdio by default. Tool results must not include content the user has
  not allowed.

**Z. Routing between models (M).** Use a small local model for quick navigation and a larger one
for summaries and planning. Config: a model per task (`AI:Models:Chat`, `AI:Models:Summary`,
`AI:Models:Embedding`). The AI section today has one `ModelId`; the translation and embedding
settings already show the pattern of a per-feature model.

**AA. Evaluation harness for the assistant (S, and needed before any L item).** A set of recorded
requests with the expected tool calls, run against a local model in the end-to-end suite with the
sandbox services (`SandboxServices`), plus a deterministic fake model for CI. This is what makes
the tool surface safe to grow. Without it, each new tool is tested only by its own unit test.

## 3. Cross-cutting concerns

### 3.1 Safety model

- **The model proposes; the user disposes.** Every write goes through `IAiActionApprovalService`.
  A new feature that writes (rename plans, sidecar files, commit messages written into a repository,
  MCP tools) needs a preview that the approval dialog can show.
- **Prompt injection from content.** File names, file text, mail bodies, web pages, container logs,
  and MCP results can all tell the model to do something. The mitigation is layered: content is
  labelled as data in the system prompt; the approval dialog shows the action, not the model's
  reasons; the tools that can destroy data stay gated by y/n on the footer.
- **Read approval.** `read_panel_file` already asks before any read. Features that send content
  in bulk (B, E with embeddings, U, V, S) need a batch approval that names the count and the
  total size, and the provider (local or remote).

### 3.2 Privacy and redaction

- A provider label (local or remote) shown next to the AI bar and in every approval that sends content.
- A redaction pass before any terminal output, log, diff, or environment dump is sent. Patterns for
  `KEY=`, `TOKEN=`, `PASSWORD=`, bearer headers, private-key blocks, and card or IBAN-like numbers.
  Redaction is best effort and must not be presented as a guarantee.
- Embeddings and the document index: keep them off by default, and show the folders they cover.

### 3.3 Threading and budgets

- Every model call runs on a background task, never in a key handler or in `Draw` (200 ms budget).
  Results land through `IsCurrentUrl()` checks and `OnIdle` polling, as the speech service does.
- Long jobs (B, F, O, R) run as `IJob`s or through `TaskManager` at `Background` priority, and they
  must finish a pass within the five-second shutdown window. Their state survives a restart only
  if it is in the database (not in a `__` key).

### 3.4 Testing

- Unit tests with a fake `IChatClient` returning scripted tool calls (the existing `AiPanelToolsTest`
  pattern).
- End-to-end: `SandboxServices` must replace anything that reaches outside the sandbox. A new
  model-backed feature needs a sandbox double for the model, so the suite never starts Ollama.
- Evaluation harness (AA) for model behaviour; it cannot be part of the default CI run because it
  needs a model. It runs on demand.

### 3.5 Cost and performance

- Remote calls cost money. Show a rough token count before a bulk operation (B, F, S, U) and
  refuse above a configured cap.
- Local models are slow on CPU-only machines. Features that need a long answer (K, L) should degrade
  to a shorter prompt when the model is slow, not block.

## 4. Suggested order

1. **AA** (evaluation harness) and **3.1**'s batch read approval: the groundwork that makes the
   rest testable and safe.
2. **K, L, H** (commit message, build/test explanation, failed command explanation): small, high
   value, all reuse existing services and need only text.
3. **C, G, E** (explain a file, search hits): extend `read_panel_file` and the `search` drive.
4. **S, T** (natural-language queries over data files): reuse the read-only SQLite guards and
   the existing viewers.
5. **A, I, J** (smart rename, command generation, command history): panel and terminal features.
6. **O, Q** (transcription of files, music helpers): local-only, no privacy cost.
7. **X, Y, Z** (MCP client, MCP server, model routing): larger platform work, after the approval and
   evaluation groundwork.
8. **B, F, P, U, V, W, R** (bulk organisation, meaning search across folders, image captions, mail,
   web, encyclopedia, video subtitles): each needs the batch approval and a privacy decision from the user.

## 5. Decisions for the user

These are not settled by the code and affect the order above.

- **Remote providers for content.** Should content (file text, mail, diffs) ever go to a remote
  provider, or only to a local Ollama? The answer decides whether U, V and B are in scope before
  the local-only options.
- **MCP.** Is an MCP client (X) wanted at all? It adds a process-running surface that the app does not
  have today.
- **Default model.** `AiOptions.ModelId` defaults to a Claude model name while the default provider
  is Ollama. Which is the intended default for a new install?
- **Embeddings on by default.** Should meaning search (F) ever be on without an opt-in? Current behaviour
  is off.

## 6. Sources

External facts used above, from a web search made while writing. The search summaries disagree with each other on
some version numbers, so these were not relied on for anything beyond the points named.

- MCP specification 2026-07-28 (stateless core, deprecations, Streamable HTTP):
  [blog.modelcontextprotocol.io/posts/2026-07-28](https://blog.modelcontextprotocol.io/posts/2026-07-28/),
  [modelcontextprotocol.io/specification/2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28.md)
- MCP C# SDK 1.0 and the Microsoft.Extensions.AI integration:
  [infoq.com/news/2026/03/mcp-csharp-v1](https://infoq.com/news/2026/03/mcp-csharp-v1/),
  [learn.microsoft.com, get started with MCP](https://learn.microsoft.com/en-ie/dotnet/ai/get-started-mcp)
- Ollama release history (version numbers conflict between sources; check the GitHub releases page):
  [releases.sh/ollama/overview.md](https://releases.sh/ollama/overview.md),
  [insiderllm.com, Ollama 0.17 features](https://insiderllm.com/guides/ollama-0-17-new-features/)

Repository sources: `iTerminal.Core/AI/*.cs`, `iTerminal.Core/Speech/*.cs`,
`iTerminal.Core/Services/Translation/LlmTranslationProvider.cs`, `docs/architecture/*.md`.
