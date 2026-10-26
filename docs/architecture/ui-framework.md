### `iConsole` — reusable terminal UI framework
General-purpose TUI infrastructure with no knowledge of file managers. Key subsystems:

- **Controls** (`iConsole/Controls/`): `Window` is the base for all interactive UI panels. Controls handle `HandleKeyEventAsync` and render into a buffer.
- **Commands** (`iConsole/Commands/Command.cs`): User actions. Override `Key`, optionally `CanExecute()`, and `ExecuteAsync(object? args)`. Commands are attached to controls and dispatched by the key router. `Command.RunAsync` wraps execution with `CanExecute` gating, exception capture, and `ICommandLogger` recording — dispatch through it, not `ExecuteAsync` directly.
- **Jobs** (`iConsole/Jobs/Job.cs`): Long-running background loops (rendering, keyboard polling, network info). Each `Job` subclass implements `RunIterationAsync`. Jobs are registered as `IJob` singletons and run on the `Desktop` lifecycle, not the `TaskManager`.
- **TaskManager** (`iConsole/Tasks/TaskManager.cs`): Priority queue (`Rendering=0 > Input=1 > Background=2`) with multiple worker threads. Used for one-shot async work (directory enumeration, file copy). `BackgroundTask` is the base; override `ExecuteAsync()` and `Priority`.
- **Data types** (`iConsole/Data/`): `Url`, `Container` (URL scheme constants), `KeyEvent`, `Position`, `Size`, `ConcurrentObservableCollection<T>` (thread-safe, `ReplaceAll` for atomic batch swaps), `ConxelString` (styled character strings for rendering).
- **Services** (`iConsole/Services/`): `IDateTimeProviderService` (cached `DateTime.Now`), `IFrequentExecutionGuardService` (throttles `PropertyChanged` to max ~20/s), `IStopwatchFactory`/`IStopwatch` (injectable timing), `IThemeService`, `IErrorPromptService`.
- **Notifications** (`iConsole/Services/INotificationService.cs`, `Jobs/NotificationJob.cs`, `Controls/Toast.cs`, `Controls/ToastHost.cs`): self-dismissing toasts posted from any thread — see "Notifications and toasts" below.
- **Diagnostics** (`iConsole/Diagnostics/`): `ICommandLogger`/`IScreenLogger` capture command executions and rendered screen snapshots for agentic/behavioural tests. `AddiConsoleCore` registers the `Null*` no-op implementations by default; `iConsole.Test.Core` provides `InMemoryCommandLogger`/`InMemoryScreenLogger`, and `AppHarness` wires those in so tests can assert on what ran and what was drawn.


The detailed write-ups, one file per subsystem:

| Part | Covers |
| --- | --- |
| [Notifications and toasts](ui-framework/notifications.md) | `INotificationService`, `NotificationJob`, `ToastHost` |
| [Small input and navigation controls](ui-framework/input-and-navigation-controls.md) | Controls for keys, lists, menus and panels |
| [Rendering pipeline](ui-framework/rendering-pipeline.md) | `ControlRendererJob` to `ConsoleBufferRendererJob`; frame dumps |
| [Lending the terminal (Ctrl+O)](ui-framework/terminal-ownership.md) | `ConsoleOwnership` and the child shell |
| [Platform input plug-ins](ui-framework/platform-input-plugins.md) | `iConsole.Inputs.Posix` and `iConsole.Inputs.Windows` |
- **Frequent-execution guard** (`FrequentExecutionGuardService`): a request that arrives within the filter window runs once the window is over, and the newest request is the one that runs. A request that replaces a waiting one does not stop the next from running; an action that throws is traced and the rest still run.
- **Periodic repaint** (`Control.StartAutoRefresh`): starting it again replaces the repaint already running, and `StopAutoRefresh` ends it.
- **Clock** (`DateTimeProviderService`): the cached time is refreshed on the monotonic clock, so setting the wall clock back cannot freeze it.

### Localization (`iConsole/Localization/`)
`ILocalizer` (registered by `AddiConsoleCore`) turns a resource key into text in the current language. Strings live in
`.resx` files, a neutral English one plus `de`, `fr`, `es`, `it` and `hu`; the framework's own are
`iConsole/Localization/Resources/Strings*.resx` (`Common.*`), and the app adds its own through an `ILocalizationSource`
(`iTerminal.Core/Localization/Resources/AppStrings*.resx`).
- **English is the neutral language and the fallback.** A key a language lacks (or leaves empty) shows the English text; a key no
  file knows shows the key itself, so a missing string is visible. `Get(key, args)` formats with the current culture and never
  throws on a broken placeholder; `TryGet` is the lookup without the key-as-text fallback.
- **The choice is `ILanguageStore`-backed.** `Localizer` reads it at construction and saves on `SetLanguage`; the app's store is the
  `Language` setting (`SettingsLanguageStore`). An unknown saved code leaves English. The default is English, never the OS culture.
- **There is no change notification in `ISettingsService`, so `ILocalizer.LanguageChanged` is the notification.** It fires on the
  caller's thread; a control that keeps looked-up text sets a flag in the handler and re-reads in `OnIdle` (the menu bar and the
  footer do), because the control tree must not be touched from another thread.
- **Menus and the footer are translated by key derived from the English text.** `MenuItem.Localize(localizer)` keeps the text an
  entry was built with and looks up `Menu.` + its label without non-alphanumerics (`Edit With…` → `Menu.EditWith`); the shortcut after the
  tab is never translated. The footer does the same with `Footer.` + the label of `F5-Copy`. An entry without a key stays English, so
  a new menu entry needs a `Menu.*` row in every `AppStrings*.resx` (the completeness test fails otherwise).
- **Adding a string:** add the key to the neutral file and to all five language files; `LocalizationResourcesTest` fails when a
  language lacks a key, has an extra one, or changes the `{n}` placeholders. Strings are extracted in stages: the menu bar, the hot key
  bar and the language dialog are done; dialog bodies, the command palette and the commands' own titles are still English.
