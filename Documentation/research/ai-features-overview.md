# AI features overview

A running record of AI features, protocols and tools that could suit iTerminal, what each needs in this codebase, and where it would live. Each survey is appended and older survey text is never rewritten. Every item is checked against the source of the branch and commit the survey names. The earlier record, `Documentation/ai-integration-opportunities.md`, is read-only: its items use letters (A to AA) and are cited here as "prior record" with the letter, and they are not repeated. Nothing here is scheduled, and each proposal is for its own `/incremental-improvement` run.

## What iTerminal already has

| Area | What is there | Where |
| --- | --- | --- |
| Chat over any model | One `IChatClient` built from `AI:Provider`: `None`, `Ollama` (the default) or `OpenAI` (any OpenAI-compatible endpoint). | `iTerminal.Core/AI/AiChatClientFactory.cs`, `AiOptions.cs` |
| Model settings | One `ModelId` for chat (default: a Claude model name, `AiOptions.cs` line 18) and one `EmbeddingModelId`. No per-task routing. | `iTerminal.Core/AI/AiOptions.cs` |
| Tools over the panels | 41 `AIFunctionFactory.Create` registrations: panel state, cursor, marks, sort, filter, refresh, copy, move, rename, delete, read, archives, compare and sync. | `AiChatService.cs`, `AiPanelTools.cs`, `AiPanelTools.ArchivesAndFolders.cs` |
| Approval for writes | Every write tool previews first; only the user's Y in the approval dialog performs it. | `iTerminal.Core/AI/AiActionApprovalService.cs` |
| File content to the model | `read_panel_file` sends the quick-view preview after approval. | `AiPanelTools.cs` |
| Meaning search over documents | A brute-force float32 vector index beside the FTS table, on when `AI:EmbeddingModelId` is set. | `iTerminal.Core/Services/Documents/DocumentVectorIndex.cs` |
| Translation by model | `LlmTranslationProvider`, used by Automatic mode after LibreTranslate. | `iTerminal.Core/Services/Translation/` |
| Voice input | Alt+F1 records; Whisper transcribes offline into the question. | `iTerminal.Core/Commands/VoiceInputCommand.cs`, `iTerminal.Core/Speech/` |
| Text from images | `TesseractOcrEngine` behind `IOcrEngine`, gated by a setting. | `iTerminal.Core/Documents/` |
| On-demand runtimes | Ollama and the Whisper model are downloaded or found, not shipped. | `iTerminal.Core/Services/RuntimeDependencies/` |

## Proposed features

| Feature | Seen in | Status in iTerminal | Fits |
| --- | --- | --- | --- |
| Typed answers from the model | Microsoft Learn, structured output quickstart | Missing | `AiChatService`, `AiChatClientFactory` |
| In-process GGUF model | LLamaSharp repository | Missing | `AiChatClientFactory`, runtime dependencies |
| Web search tool for the assistant | Ollama web search docs | Missing | `AiPanelTools` |
| Vector search inside SQLite | sqlite-vec repository | Partly present (brute-force index) | `DocumentVectorIndex` |
| Answers read aloud | SpeechSynthesizer reference, Piper repository | Missing | `iTerminal.Core/Speech/` |
| Host an external coding agent over ACP | ACP overview and transports | Missing | `iTerminal.Core/AI/` |
| Reusable instruction folders (SKILL.md) | Agent Skills specification | Missing | data folder, `AiChatService` |
| Traces of model calls (OpenTelemetry GenAI) | Microsoft.Extensions.AI reference, OpenTelemetry GenAI spans | Missing | `AiChatClientFactory` |

## Suggested order

The order for each survey's new items is in that survey. The order for survey 1 is:

1. Item 1 (typed answers): small, and it gives the plan types that prior record A, B and S need.
2. Item 8 (traces of model calls): small and local, and it shows what the other features send.
3. Item 6 (ACP): the largest gain, but its permission and file methods must go through the approval path first.
4. Item 7 (instruction folders): useful with no new data flow.
5. Item 5 (read aloud): local with a system voice, and opt-in.
6. Item 3 (web search tool): sends queries out, so it needs an opt-in and a visible remote label first.
7. Item 2 (in-process model): large native packages; only worth it if a model without a server is wanted.
8. Item 4 (sqlite-vec): pre-v1, and the brute-force index works at the current library size.

## Surveys

### November 2026

#### Survey 1: agent protocols, instruction files, structured output, speech, local models, search and tracing

**Topics and basis.** Eight candidates: the Agent Client Protocol, Agent Skills, structured output in Microsoft.Extensions.AI, LLamaSharp, the Ollama web search API, sqlite-vec, text to speech (System.Speech and Piper), and the OpenTelemetry GenAI conventions with the Microsoft.Extensions.AI extension. They were read on 2026-11-20 from official sources (specifications, API references, repository pages and licences), not from run-time behaviour; nothing was built or run. Versions and dates are as each page states them. Facts taken from search snippets are marked unverified.

**Not found.** A case-insensitive grep of `iConsole/` and `iTerminal.Core/` (`*.cs`) on branch `docs/research-overviews` at commit `41d889b2`. The terms are listed under each missing item.

**Research table**

| Candidate | Kind | Official link | Version and date | Features and licence (source for each) |
| --- | --- | --- | --- | --- |
| Agent Client Protocol | open protocol, JSON-RPC 2.0 | [agentclientprotocol.com](https://agentclientprotocol.com/protocol/overview) | protocol v1 pages read 2026-11-20; no version or date on the page; a v2 draft is named in search results (unverified) | Client starts the agent as a subprocess; `initialize`, `session/new`, `session/prompt`, `session/update`, `session/request_permission`, and optional `fs/read_text_file` and `fs/write_text_file` ([overview](https://agentclientprotocol.com/protocol/overview)); stdio is the only transport defined, one JSON message per line ([transports](https://agentclientprotocol.com/protocol/transports)); Apache-2.0, five official SDKs and none for .NET ([repository](https://github.com/agentclientprotocol/agent-client-protocol)) |
| Agent Skills | open format | [agentskills.io](https://agentskills.io/specification) | no version on the spec page; no release on the repository page; read 2026-11-20 | `SKILL.md` with YAML frontmatter; `name` (up to 64 characters, matching the folder) and `description` (up to 1024) required; `license`, `compatibility`, `metadata` and the experimental `allowed-tools` optional; `scripts/`, `references/` and `assets/` directories; progressive loading; a `skills-ref` validator ([spec](https://agentskills.io/specification)); code Apache-2.0, docs CC-BY-4.0 ([repository](https://github.com/agentskills/agentskills)) |
| Structured output in Microsoft.Extensions.AI | library feature | [Microsoft Learn](https://learn.microsoft.com/dotnet/ai/quickstarts/structured-output) | page updated 2026-03-13 (page metadata); read 2026-11-20 | `GetResponseAsync<T>` on `IChatClient` returns a typed `Result` ([quickstart](https://learn.microsoft.com/dotnet/ai/quickstarts/structured-output)); a manual schema helper and provider support are unverified (search snippets) |
| LLamaSharp | .NET library, MIT | [GitHub](https://github.com/scisharp/LLamaSharp) | v0.30.0 in the version table; no release date shown; read 2026-11-20 | GGUF models through llama.cpp; CPU, CUDA 12, CUDA 13 and Vulkan backends as separate packages; model and backend versions must match ([repository](https://github.com/scisharp/LLamaSharp)); `IChatClient` support not stated (a third-party wrapper, [Hearth.AI](https://www.nuget.org/packages/Hearth.AI), from a search snippet, unverified) |
| Ollama web search | hosted REST API, API key required | [Ollama docs](https://docs.ollama.com/capabilities/web-search) | no date on the page; read 2026-11-20 | `POST https://ollama.com/api/web_search`; key as a Bearer token or in `OLLAMA_API_KEY`; `query` required, `max_results` default 5 and maximum 10; results with `title`, `url` and `content`; the page does not say where queries are processed ([docs](https://docs.ollama.com/capabilities/web-search)) |
| sqlite-vec | SQLite extension, C | [GitHub](https://github.com/asg017/sqlite-vec) | pre-v1 per the README; latest alpha 0.1.10-alpha.4 per [releases](https://github.com/asg017/sqlite-vec/releases), date unverified (search snippet) | KNN query with `match` and `order by distance` ([repository](https://github.com/asg017/sqlite-vec)); Apache-2.0 and MIT ([repository](https://github.com/asg017/sqlite-vec)); IVF and DiskANN files in the tree, status unverified |
| Text to speech (System.Speech) | framework API | [SpeechSynthesizer](https://learn.microsoft.com/dotnet/api/system.speech.synthesis.speechsynthesizer) | page updated 2026-07-01; package `System.Speech` v11.0.0; read 2026-11-20 | Installed voices, `SpeakAsync` and `SpeakAsyncCancelAll`, output to the default device or a wave file or stream, progress events ([reference](https://learn.microsoft.com/dotnet/api/system.speech.synthesis.speechsynthesizer)); platform support not stated on the page read (unverified) |
| Piper | local neural TTS engine | [GitHub](https://github.com/OHF-Voice/piper1-gpl) | no release shown on the fetched page; search snippets name v1.7.0 and v1.8.0 (unverified); read 2026-11-20 | GPL-3.0 licence ([repository](https://github.com/OHF-Voice/piper1-gpl)); voice licences are per model, listed MIT for the rhasspy voices ([voices README](https://huggingface.co/rhasspy/piper-voices/blob/main/README.md), search snippet, unverified); the original rhasspy repository was archived in October 2025 (search snippet, unverified) |
| OpenTelemetry GenAI conventions | semantic conventions, Development status | [spans](https://raw.githubusercontent.com/open-telemetry/semantic-conventions-genai/main/docs/gen-ai/gen-ai-spans.md) (the opentelemetry.io page has moved; read 2026-11-20) | status Development; the links name v1.45.0; no date on the page | Operation name and provider name are required on the fetch span example; message content capture is opt-in and off by default; the span name is the operation name ([spans](https://raw.githubusercontent.com/open-telemetry/semantic-conventions-genai/main/docs/gen-ai/gen-ai-spans.md)) |
| Microsoft.Extensions.AI OpenTelemetry | library API | [reference](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.opentelemetrychatclientbuilderextensions.useopentelemetry) | package v11.0.0; page updated 2026-11-11 (page metadata); read 2026-11-20 | `UseOpenTelemetry` on `ChatClientBuilder` with an optional logger factory, source name and configure callback; follows the experimental GenAI conventions ([reference](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.opentelemetrychatclientbuilderextensions.useopentelemetry)); the content switch is unverified (search snippet) |

**Groups**

**Assistant**

1. **Typed answers from the model.** Missing: a call that asks the model for a typed result (a list of rename rows, one value from a set) and parses it, instead of free text. Every call today is a plain `GetResponseAsync` over messages (`AiChatClientFactory.cs`, `LlmTranslationProvider.cs`). Fits: `AiChatService` and `AiChatClientFactory`, with the plan types that prior record A, B and S need. Data: the same content and the same endpoint as chat, so nothing new leaves the machine. Opt-in: none beyond the AI setting. Caveat: whether Ollama's `/v1` endpoint accepts a JSON schema response format is unverified; the quickstart uses a provider with that support. **Seen in:** [Microsoft Learn quickstart](https://learn.microsoft.com/dotnet/ai/quickstarts/structured-output). Search terms (not found): `GetResponseAsync<`, `ChatResponseFormat`, `JsonSchema`, `AIJsonUtilities`, `response_format`.

2. **An in-process model (LLamaSharp).** Missing: a chat client that loads a GGUF file in the app's own process. The only local model today is an Ollama server reached over HTTP (`OllamaServer.cs`). Search terms (not found across the tree): `LLamaSharp`, `\bLLama\b`, `gguf`, `llama.cpp`, `InferenceExecutor`; the one `llama` match is a model name in a test. Fits: an `AiProvider` case beside `Ollama`, the native backend as a runtime dependency, and the model file pinned by size and hash as `WhisperRuntimeDependency` does. Data: nothing leaves the machine. Caveats: the README says the backend and model must match the library version; the native size is not stated; whether the library implements `IChatClient` is not stated in the README. Licence: MIT. **Seen in:** [LLamaSharp repository](https://github.com/scisharp/LLamaSharp).

**Search and documents**

3. **A web search tool for the assistant.** Missing: an AI tool that runs a web search and returns titles, links and snippets. Partly present: the web browser's address bar takes a search URL template (`WebBrowserStore`). The other web matches in the source (forecast provider, encyclopedia, browser pages) are not AI tools. Search terms (not found as an AI tool): `web_search`, `websearch`, `web search`, `SearchEngine`, `duckduckgo`, `SearchUrl`, in `iTerminal.Core/AI/` and across `iTerminal.Core/`. Fits: a `web_search` entry in `AiPanelTools.cs`, with the key from the vault or the password manager and never in a URL (`CLAUDE.md`). Data: the query text goes to ollama.com, and the page does not say where queries are processed. Opt-in: a setting, default off, and the AI bar must show that the search is a remote service. Results are page text, so they are data, and the prompt must say so (prior record 3.1). A search is not a write, so it needs no approval dialog. **Seen in:** [Ollama web search docs](https://docs.ollama.com/capabilities/web-search) (read 2026-11-20).

4. **Vector search inside SQLite (sqlite-vec).** Partly present: `DocumentVectorIndex` keeps one float32 vector per passage in raw SQL tables and ranks by brute force (`media-and-documents.md` puts the memory at about 120 MB for 10,000 documents). Missing: a nearest-neighbour search that runs inside SQLite, loaded as an extension. Search terms (not found): `sqlite-vec`, `vec0`, `LoadExtension`, `knn`, `approximate nearest`. Fits: `DocumentVectorIndex`, behind the same methods, with the virtual table created on demand as the FTS table is, so the EF model stamp is untouched. The extension is native code, so it would be a runtime dependency and not shipped. Data: local only; what is sent for embeddings does not change. Caveats: the README says the project is pre-v1 and that breaking changes are expected; the file list names IVF and DiskANN files, but the README does not describe them (unverified). Licence: Apache-2.0 or MIT. **Seen in:** [sqlite-vec repository](https://github.com/asg017/sqlite-vec).

**Speech and translation**

5. **Answers read aloud.** Missing: any speech output. `iTerminal.Core/Speech/` only records and recognises. Search terms (not found): `SpeechSynthesizer`, `text-to-speech`, `TTS`, `SpeakAsync`, `speech-dispatcher`, `spd-say`, `piper`, `read aloud`, `speech output`. The only hits across the whole tree were names that contain the letters "sapi" (for example WASAPI). Fits: `iTerminal.Core/Speech/`, beside `SpeechToTextService`, one utterance at a time on a background task, and Esc cancels it through the cancel-all call. On Windows the API is `System.Speech`, with installed voices and asynchronous speaking; the platform requirement is not stated on the page read (unverified). Linux and macOS engines were not researched in this survey. Piper is a local neural engine, but its repository is GPL-3.0, so it would be downloaded like Whisper and kept out of the installer until its licence has been reviewed. Data: nothing leaves with a local voice; a remote voice would need an opt-in. **Seen in:** [SpeechSynthesizer reference](https://learn.microsoft.com/dotnet/api/system.speech.synthesis.speechsynthesizer), [Piper repository](https://github.com/OHF-Voice/piper1-gpl).

**Agents and tools**

6. **Host an external coding agent (ACP).** Missing: a client that starts an agent process and exchanges messages with it. The AI bar talks only to its own model. Search terms (not found): `agent client protocol`, `\bACP\b`, `session/prompt`, `claude code`, `coding agent`, `external agent`, `StreamJsonRpc`, in `iTerminal.Core/` and `iConsole/`. `iConsole/Lsp/JsonRpcConnection.cs` already does JSON-RPC over two streams, but with LSP's `Content-Length` framing. ACP sends one JSON message per line, so the request matching could be reused and the framing could not. Fits: a client in `iTerminal.Core/AI/`, the agent started through `IExternalProcessRunner` on a background task and killed on exit, as the recorders are. The agent's permission requests go to the existing approval dialog. Its file methods are offered only through the read and write approvals that already exist. Data: whatever the chosen agent sends to its own provider. The user names the command in Settings and opts in per agent. Caveats: the page lists five official SDKs and none for .NET; the v1 pages were read, and the v2 draft named in search results is not relied on. **Seen in:** [ACP overview](https://agentclientprotocol.com/protocol/overview), [ACP transports](https://agentclientprotocol.com/protocol/transports), [ACP repository](https://github.com/agentclientprotocol/agent-client-protocol).

7. **Instruction folders (Agent Skills).** Missing: no loading of `SKILL.md` files or other user instructions. The system prompt is one static string (`AiChatService.cs`, line 22). Search terms (not found): `SKILL.md`, `agent skill`, `skills`, `prompt template`, `saved prompt`, `PromptTemplate`. Fits: a folder in the data folder beside `usermenu.txt` (`UserMenuService`), read when a question is sent and never on a key handler. The spec's progressive loading fits: each skill's `name` and `description` go into the prompt, and its body is added only when the skill is used. `allowed-tools` is experimental and would be a hint only; the approval stays. `scripts/` never runs without an approval dialog. Data: skill text goes to whichever model is configured, so a remote provider sees it. Opt-in: the user adds each folder; nothing ships with skills. Caveat: the spec requires `name` to match the folder name. **Seen in:** [Agent Skills specification](https://agentskills.io/specification), [Agent Skills repository](https://github.com/agentskills/agentskills) (code Apache-2.0, docs CC-BY-4.0).

**Framework**

8. **Traces of model calls (OpenTelemetry GenAI).** Missing: no tracing around model calls. There is no OpenTelemetry package or `ActivitySource`, and the AI folder does not log through `ILogger`. Search terms (not found across the tree): `ActivitySource`, `OpenTelemetry`, `gen_ai`, `StartActivity`, `System.Diagnostics.Activity`, and `ILogger` or `Log.` in `iTerminal.Core/AI/`. Fits: `UseOpenTelemetry` on the chat client in `AiChatClientFactory`. `Microsoft.Extensions.AI` 11.0.0 is already referenced in `iTerminal.Core.csproj`, so no new package is needed; the search of the project files found no OpenTelemetry reference. Use a logger factory from the app's Serilog setup (`CLAUDE.md`: logging goes through Serilog, `Serilog` 4.4.0 is referenced) and no exporter unless the user configures one. Data: nothing leaves unless an exporter is set. Message content stays out of spans, since the convention makes content capture opt-in; the content switch and its environment variable are unverified. Caveats: the conventions are Development status and may change; one search snippet gives the library's convention version as v1.41 and another as v1.37 (unverified). **Seen in:** [Microsoft.Extensions.AI UseOpenTelemetry reference](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.opentelemetrychatclientbuilderextensions.useopentelemetry), [OpenTelemetry GenAI span conventions](https://raw.githubusercontent.com/open-telemetry/semantic-conventions-genai/main/docs/gen-ai/gen-ai-spans.md).

**Suggested order (new items in survey 1)**

1. Item 1, typed answers: small, and it unblocks the plan features in prior record A, B and S.
2. Item 8, traces of model calls: small, local, and it makes the other items observable.
3. Item 6, ACP: the largest new capability; the permission path must be built first.
4. Item 7, instruction folders: no new data flow.
5. Item 5, read aloud: local, opt-in.
6. Item 3, web search tool: remote queries, so opt-in and a visible label first.
7. Item 2, in-process model: large native packages; a later choice.
8. Item 4, sqlite-vec: pre-v1; the brute-force index is enough at the current size.

**Already covered (no new item)**

- Chat with a local model or any OpenAI-compatible endpoint, and the Ollama runtime: present (see "What iTerminal already has").
- An MCP client or server: prior record X and Y. The source has no MCP reference (search terms `ModelContextProtocol`, `McpClient`, `McpServer`, `\bmcp\b` in `iTerminal.Core/`, none in the AI code).
- Routing between models per task: prior record Z. `AiOptions` has one `ModelId` and one `EmbeddingModelId`.
- Image input and captions: prior record P. Search terms `ImageContent`, `DataContent`, `UriContent`, `vision` in `iTerminal.Core/AI/` found nothing.
- An evaluation harness: prior record AA.
- Summaries of a web page: prior record V.
- Note: ACP (item 6) is a client of an agent that the user runs; prior record Y is an MCP server that exposes iTerminal. They are different protocols and both stay.

#### Status after the pull of 2026-11-20

- Prior record, section 1, "About 60 AITools": a grep finds 41 `AIFunctionFactory.Create(` registrations in `iTerminal.Core/AI/` (`AiChatService.cs` 13, `AiPanelTools.cs` 23, `AiPanelTools.ArchivesAndFolders.cs` 5) at commit `41d889b2`. The count in the prior record does not match. The prior record is read-only and is not edited.
- Prior record, section 1, "Voice input (Alt+F1)": `VoiceInputCommand` binds only Alt+F1, but the AI bar's hint row lists Ctrl+F7 for the same action (`iTerminal.Core/Controls/Terminals/AiPromptBar.cs`, line 262). Ctrl+F7 is the music panels key (`iTerminal.Core/Commands/Music/MusicPanelsCommand.cs`). The feature is unchanged; the hint text is wrong.
- Checked and unchanged: prior record X and Y (no MCP), P (no image content types), Z (one chat `ModelId`), the default provider `Ollama` and the Claude model name in `AiOptions` (lines 17 and 18).
