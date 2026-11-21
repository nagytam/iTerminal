# Architecture overview

This document holds the architect reviews of iTerminal, one section per scope. A later review of the same scope replaces its section. Finding numbers are unique across the whole document and are never reused; a finding that is fixed moves to the **Resolved** list at the end with its number.

## Dependencies and packages

Reviewed on 2026-11-21, scope "Dependencies and packages", branch `promotion-survey-2`, commit `a0f03502`. Read: `CLAUDE.md`, `iTerminal.slnx`, every `*.csproj`, the `.wixproj` and the Linux `.proj`, `Directory.Build.props`, both workflows in `.github/workflows/`, `runtime-dependencies.md`, `build-and-packaging.md`, `platform-input-plugins.md`, the package lines of the other architecture pages, the research documents in this folder, and the source files that use each package (found with `grep` on the namespace). Run: `dotnet list iTerminal.slnx package` with `--outdated`, `--vulnerable --include-transitive`, `--deprecated` and `--include-transitive`, and `--outdated --include-prerelease` for `iConsole` and `iTerminal.Core`, all against nuget.org on SDK 11.0.100; the restore they ran touched only `obj/` folders. Not done: no build, no test run, no run-time check. Not covered by those commands: the WiX packages (the `.wixproj` is not a PackageReference project for `dotnet list`) and `Packaging.Targets` (referenced only when `LinuxPackaging` is `true`), so their latest versions are **unverified**. There is no `global.json`, no `Directory.Packages.props` and no `Directory.Build.targets`.

**Summary**

- No package has a known vulnerability (direct or transitive), and every project targets `net11.0`. Only one package is deprecated (`imobiledevice-net`, finding 4).
- The biggest structural risk is that `Svg.Skia` is referenced at 3.0.0 in `iConsole` and 5.2.3 in `iTerminal.Core`: `iConsole`'s SVG code is compiled and tested against one major version and ships with another, and its catch-all hides a failure (finding 1).
- The releases ship about 40 third-party assemblies, some under LGPL-3.0-or-later and MPL-2.0, with no notice file, and the packaging documents still say the repository has no licence (finding 2).
- Versions are written by hand in up to seven project files each, with no Central Package Management, lock files or audit gate (findings 5 and 6). The `Svg.Skia` split is the cost of that.
- Strong parts: the Playwright driver is kept out of every package and installed against the exact library version with a NuGet SHA-512 check; the Whisper model is pinned by URL, size and hash; every GitHub download refuses an asset without a digest; and the Linux dependency lists are owned by the project rather than the packaging tool.

**Findings**

1. **`Svg.Skia` is at two major versions, so `iConsole`'s SVG rendering is tested against a stack that is never shipped** — **High**, kind: dependency, consistency.
   - **Where:** [iConsole/iConsole.csproj:26](../../iConsole/iConsole.csproj) (`Svg.Skia` 3.0.0), [iTerminal.Core/iTerminal.Core.csproj:49](../../iTerminal.Core/iTerminal.Core.csproj) (`Svg.Skia` 5.2.3). Users: [iConsole/Services/FileToTextConverterService.cs:808](../../iConsole/Services/FileToTextConverterService.cs) (`RenderSvg`, compiled against 3.0.0) and [iTerminal.Core/Video/SvgVideoDecoder.cs:2](../../iTerminal.Core/Video/SvgVideoDecoder.cs) (5.2.3). Tests: `iConsole.Test/Services/SvgRenderingTest.cs` (resolves 3.0.0) and `iTerminal.Core.Test/Video/SvgVideoDecoderTest.cs` (resolves 5.2.3).
   - **Problem:** NuGet picks the nearest reference, so the app and `iTerminal.Core.Test` run `RenderSvg` on `Svg.Skia` 5.2.3 with `HarfBuzzSharp` 14.2.0, while `iConsole`, `iConsole.Test` and both input plug-ins resolve 3.0.0, which pulls `SkiaSharp.HarfBuzz` 3.116.1 and `HarfBuzzSharp` 8.3.0.1 (built for SkiaSharp 3) next to SkiaSharp 4.156.0, and has no Linux HarfBuzz native asset. `RenderSvg` catches every exception except `OutOfMemoryException` ([FileToTextConverterService.cs:833](../../iConsole/Services/FileToTextConverterService.cs)), so a missing member at run time would quietly turn the F3 SVG view into plain text, and the only tests of that method run against the other version. This breaks the "one problem, one solution" consistency principle and makes the shipped path untested. Whether 5.2.3 still has every member `RenderSvg` calls was not checked: **unverified**.
   - **Improvement:** move the single `Svg.Skia` reference to `iConsole` at 5.2.3 (where the lower layer needs it), and remove it from `iTerminal.Core`, which then gets it transitively like SkiaSharp. If `iConsole` should not carry it (finding 8), move `RenderSvg` up instead; either way one project owns the version.
   - **Effort:** S.
   - **Risk of the change:** a source break in `RenderSvg` if the 5.x API differs; the build shows it, and `SvgRenderingTest`, `SvgVideoDecoderTest` and the F3 image viewer tests in `iTerminal.Test` catch a behaviour change. `iConsole` is not packed as a NuGet package (no `PackageId`, not packable by any workflow), and no project outside this solution was found that references it, so no outside caller is affected.

2. **The releases ship third-party code under notice-requiring licences without any notice file** — **Medium**, kind: dependency, documentation.
   - **Where:** the licence fields of the cached packages (`nuspec`): `SMBLibrary` 1.5.8.1 LGPL-3.0-or-later, `OpenMcdf` 3.3.0 MPL-2.0, `PdfPig` 0.1.16 and `Serilog.Sinks.File` Apache-2.0, `Tomlyn` BSD-2-Clause, `imobiledevice-net` 1.3.17 a licence URL only (its native `libimobiledevice` files are LGPL), most others MIT. Release artifacts: [iTerminal.Installer/Package.wxs:37](../../iTerminal.Installer/Package.wxs) (licence page skipped because "the repository has no licence text"), [build-and-packaging.md:12](../architecture/build-and-packaging.md) and `:34` (RPM licence field "iterminal License"), while [LICENSE](../../LICENSE) is an MIT licence present since the first public commit. No `THIRD-PARTY-NOTICES` or similar file exists in the repository or the publish output.
   - **Problem:** MIT, BSD and Apache-2.0 ask for the copyright and licence text to travel with a binary distribution, MPL-2.0 and LGPL ask for more (where the source is, and for LGPL that the library can be replaced). The MSI, the deb, the rpm and the portable archives carry none of it, and the installer and the package metadata state the project's own licence wrongly. This is a release obligation that grows with every package added; it is a reading of the licence fields, not legal advice.
   - **Improvement:** generate a `THIRD-PARTY-NOTICES.txt` at publish time from the restored packages (the `project.assets.json` of `iTerminal` lists every package and path; a small MSBuild target or a step in `release.yml` copies each package's licence file), place it next to `iTerminal.exe`, and show the project licence in the installer (`License.rtf`) and the RPM licence field (`MIT`). The SBOM writer in `DotnetSolutionService.GenerateSbom` (security overview, inventory item 4) already walks a solution's packages and could be the source of the list.
   - **Effort:** M.
   - **Risk of the change:** none for the app; a packaging step can fail a release build, which the "Release builds" pipeline shows. Check the MSI with `msiexec /a` and the deb with `dpkg -c` for the new file.

3. **The log sink is a development pre-release** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:38](../../iTerminal.Core/iTerminal.Core.csproj) (`Serilog.Sinks.File` `8.0.0-dev-02318`, since the first public commit `6a1069a2`). The only use is `.WriteTo.File(path)` in [iTerminal.Core/Logger/Logger.cs:33](../../iTerminal.Core/Logger/Logger.cs).
   - **Problem:** every release ships a CI build of the sink that was never published as stable; nuget.org now lists a different pre-release (`8.0.0-nblumhardt-02322`) on the same number, so "8.0.0" is a moving name, and `dotnet list --outdated` cannot report a stable update for it because the stable line is below it. The one call used exists in every stable version. Principle: shipped dependencies are stable unless a feature needs otherwise.
   - **Improvement:** reference the newest stable `Serilog.Sinks.File` (7.x; the exact number was not listed by the commands run, so it is **unverified**) and keep `Serilog` 4.4.0.
   - **Effort:** S.
   - **Risk of the change:** a downgrade of a pre-release; the build and `iTerminal.Core.Test` (the `Logger` tests) show any API gap. NuGet reports NU1605 if another package needs the newer one.

4. **`imobiledevice-net` is deprecated and its Linux native files are never shipped; the warning that says so is silenced** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:24](../../iTerminal.Core/iTerminal.Core.csproj), [Directory.Build.props:5](../../Directory.Build.props) (`NETSDK1206` suppressed for the whole solution), [iTerminal.Core/Services/IPhoneDeviceService.cs:37](../../iTerminal.Core/Services/IPhoneDeviceService.cs) (a failure to load shows no devices), [panels-and-drives.md:26](../architecture/panels-and-drives.md) (`iphone://`).
   - **Problem:** `dotnet list --deprecated` reports the package as "Legacy" with no alternative. Its native files are for `win-x86`, `win-x64`, `osx-x64` and `ubuntu.16.04-x64` only; .NET 8 and later ignore the Ubuntu identifier, so the `linux-x64` and `linux-arm64` packages carry no `libimobiledevice`, and on Linux the `iphone://` drive can only work with a system library of a matching name. The suppressed warning is the build's only signal of that, and the catch-all in the service turns it into an empty device list. Whether a system library is picked up was not tried: **unverified**.
   - **Improvement:** decide the platform contract of the iPhone drive first. If it is Windows only (and macOS x64), say so in `panels-and-drives.md` and hide the drive elsewhere through `OperatingSystem` checks, and scope the `NoWarn` to `iTerminal.Core.csproj` with a comment that names the effect. If Linux matters, the replacement needs a maintained binding, which is a new dependency decision for the maintainer.
   - **Effort:** M.
   - **Risk of the change:** hiding the drive changes the drive list on Linux; the drive-selector tests in `iTerminal.Core.Test` and the Places page tests in `iTerminal.Test` show it.

5. **No Central Package Management: each version is written in up to seven project files** — **Medium**, kind: dependency, consistency.
   - **Where:** no `Directory.Packages.props`. `MSTest.TestFramework` 4.5.3 in 7 project files, `MSTest.TestAdapter` 4.5.3 and `Microsoft.NET.Test.Sdk` 18.12.0 in 6, `coverlet.collector` 10.1.0 in 4, `Moq` 4.21.0 and `Microsoft.Playwright` 1.64.1 in 2 ([iTerminal.Core/iTerminal.Core.csproj:31](../../iTerminal.Core/iTerminal.Core.csproj), [iTerminal.Core.Test/iTerminal.Core.Test.csproj:26](../../iTerminal.Core.Test/iTerminal.Core.Test.csproj)), nine `Version="11.0.0"` lines for the Microsoft packages that move with the runtime, and the WiX version 5.0.2 four times in [iTerminal.Installer/iTerminal.Installer.wixproj:20](../../iTerminal.Installer/iTerminal.Installer.wixproj), `:31`, `:32` and `:57`.
   - **Problem:** an update means editing every copy by hand, and a missed copy is silent: finding 1 is that failure. The Playwright pair matters more than the rest, because `PlaywrightRuntimeDependency.LibraryVersion()` ([PlaywrightRuntimeDependency.cs:70](../../iTerminal.Core/Services/RuntimeDependencies/PlaywrightRuntimeDependency.cs)) installs the driver for the library version, so a test project on another version tests a driver the app never asks for. Principle: one place per decision.
   - **Improvement:** add `Directory.Packages.props` with `ManagePackageVersionsCentrally` and one `PackageVersion` per package, remove the `Version` attributes from the `PackageReference` items, and keep the WiX SDK version in one MSBuild property used by both `Import` elements and the two extensions. `Packaging.Targets` can stay conditional; CPM allows a `PackageVersion` that a build does not use.
   - **Effort:** M (13 project files, mechanical).
   - **Risk of the change:** restore resolves exactly as before if the versions are copied as they are; `dotnet list package --include-transitive` before and after must match. Build and run all three test projects; the Windows job checks the installer.

6. **Restore has no guard rails: no lock files, no audit gate, no update automation, no source mapping** — **Medium**, kind: dependency.
   - **Where:** no `packages.lock.json`, no `NuGetAuditMode`, `NuGetAuditLevel` or `WarningsAsErrors` for `NU1901`–`NU1904` in any project file, no `nuget.config`, no `.github/dependabot.yml`; both workflows restore from the default feed ([.github/workflows/build.yml:32](../../.github/workflows/build.yml)).
   - **Problem:** today `--vulnerable --include-transitive` is clean, but nothing makes a future advisory fail a pull request: the SDK's audit only warns, and a warning in a 40-package restore is easy to miss. Without lock files a release build can resolve a different transitive set from the one tested, and without package source mapping any feed added later is searched for every package. The research proposals for an OSV package check (security overview, "security-tools 3.6") look at users' projects; this is the same question for iTerminal's own build.
   - **Improvement:** in `Directory.Build.props` set `NuGetAuditMode` to `all` and turn `NU1903` and `NU1904` (high and critical) into errors; add `RestorePackagesWithLockFile` with `--locked-mode` in the two workflows; add a `nuget.config` with one source and `packageSourceMapping`; add a Dependabot `nuget` entry (or a weekly `dotnet list package --outdated --vulnerable` job) so updates are proposed as pull requests.
   - **Effort:** S for audit and source mapping, M with lock files (one per project and runtime identifier for the self-contained publishes).
   - **Risk of the change:** lock files fail a restore when a version changes without the file; the build workflow shows it at once. An audit error can block a release on an advisory without a fix, so the level is the maintainer's call.

7. **Runtime downloads follow four different version policies, and two of them float** — **Medium**, kind: dependency, consistency.
   - **Where:** [OllamaRuntimeDependency.cs:27](../../iTerminal.Core/Services/RuntimeDependencies/OllamaRuntimeDependency.cs) and `:75` (`Built` 0.40.4, any 0.40.x accepted, the newest offered), [PowerShellRuntimeDependency.cs:24](../../iTerminal.Core/Services/RuntimeDependencies/PowerShellRuntimeDependency.cs) and `:84` (`Built` 7.7.0, any 7.x), [WhisperRuntimeDependency.cs:24](../../iTerminal.Core/Services/RuntimeDependencies/WhisperRuntimeDependency.cs) (pinned URL, size and SHA-256), [PlaywrightRuntimeDependency.cs:70](../../iTerminal.Core/Services/RuntimeDependencies/PlaywrightRuntimeDependency.cs) (the library's own version, NuGet SHA-512), and the package-manager tools (git, ffmpeg, yt-dlp, rar), [runtime-dependencies.md:5](../architecture/runtime-dependencies.md) to `:21`.
   - **Problem:** for Ollama and PowerShell what a user installs depends on the day: the newest release of the line, checked only against the digest GitHub records next to the file, so a release cannot say which version it was tested with, and one compromised account changes the file and its digest together (security overview, item 27, which covers the self-update and not these). The versions sit as constants in four classes, so "which runtime versions does release N use" has no single answer. Principle: explicit contracts and reproducibility. The Playwright and Whisper policies are the sound ones.
   - **Improvement:** give `OllamaRuntimeDependency` and `PowerShellRuntimeDependency` a pinned default (version plus the SHA-256 of each platform asset, verified once by a real download, as for Whisper) installed by default, and keep "newer on the same line" as the offered update that the user accepts. Put the pins of all four downloaded dependencies in one internal `RuntimeDependencyPins` class, so a release note can list them and a test can check each pin has a hash for every platform asset.
   - **Effort:** M.
   - **Risk of the change:** a wrong pinned hash refuses the install; `VerifiedDownload` already deletes a mismatch. The `*RuntimeDependencyTest` classes in `iTerminal.Core.Test` fake the feeds and cover the line rule; the end-to-end suite replaces the service with `SandboxServices.Dependencies`, so it does not reach the change.

8. **`iConsole` carries eleven file-format packages for eleven of its 543 source files** — **Medium**, kind: layering, coupling.
   - **Where:** [iConsole/iConsole.csproj:16](../../iConsole/iConsole.csproj) to `:29`. The users are `Pdf/PdfSignatureReader.cs`, `Photoshop/PsdDocument.cs`, `Services/AccessDatabaseTextReader.cs`, `Services/Disassembly/NativeDisassembler.cs`, `Services/FileToTextConverterService.cs` (1 163 lines), `Services/HtmlTextExtractor.cs`, `Services/LegacyOfficeTextRenderer.cs`, `Services/OfficeTextRenderer.cs`, `Services/OutlookMessageReader.cs`, `Toml/TomlDocumentReader.cs` and `Yaml/YamlDocumentReader.cs`. `iTerminal.Core` uses SkiaSharp in nine files with no reference of its own.
   - **Problem:** every project that references the UI framework, including both input plug-ins and their tests, restores and copies SkiaSharp with its native assets, `Svg.Skia`, HarfBuzz, OpenXml, PdfPig, Iced and the rest (the transitive list of `iConsole.Inputs.Posix` shows them). A package decision about a viewer is therefore a decision about the framework, which is how finding 1 arose. `CLAUDE.md` asks for "reusable primitives in iConsole", and format readers are reusable, so this is a cohesion question, not a rule breach. Whether it costs start-up time was not measured (profiling overview, finding 4, does not separate assembly loading): **unverified**.
   - **Improvement:** move the eleven files and their packages into a new `iConsole.Formats` project that references `iConsole`, referenced by `iTerminal.Core` and registered from `iConsoleApplicationBuilderExtension` as today; give `iTerminal.Core` its own `SkiaSharp` reference. The public interfaces (`IFileToTextConverterService` and the readers) keep their namespaces, so callers change only their project reference. The same project is the place for the "large native packages" of an in-process model (AI overview, item 2) if that is ever adopted.
   - **Effort:** L.
   - **Risk of the change:** `InstantiateAllTypesTest` must still find every moved public type, so the new project must be in the scan; `iConsole.Test` needs a reference to it. Build plus all three test projects.

9. **`Microsoft.FeatureManagement` is referenced and never used** — **Low**, kind: dependency.
   - **Where:** [iConsole/iConsole.csproj:21](../../iConsole/iConsole.csproj). The only feature flag is read as plain configuration: `configuration.GetValue<bool>("FeatureManagement:WindowsConsoleDriver")` in [iConsole.Inputs.Windows/Extensions/BuilderExtension.cs:27](../../iConsole.Inputs.Windows/Extensions/BuilderExtension.cs). No source file uses the `Microsoft.FeatureManagement` namespace, `AddFeatureManagement` or `IFeatureManager`.
   - **Problem:** every consumer of `iConsole` restores and ships the package and its dependencies for nothing, and a reader assumes the flag goes through the library. Principle: no unused dependencies.
   - **Improvement:** remove the reference; keep the `FeatureManagement` section name in `appSettings.json` so existing settings still apply.
   - **Effort:** S.
   - **Risk of the change:** a transitive package it brought may be needed elsewhere; the build shows a missing type. `BuilderExtensionCoverageTest` covers the flag.

10. **`iTerminal.Core` references the Windows input plug-in and uses none of it** — **Low**, kind: layering.
    - **Where:** [iTerminal.Core/iTerminal.Core.csproj:55](../../iTerminal.Core/iTerminal.Core.csproj). No file in `iTerminal.Core` uses a type or namespace of `iConsole.Inputs.Windows`; only [iTerminal/Program.cs:4](../../iTerminal/Program.cs) does, and `iTerminal.csproj` already references the plug-in.
    - **Problem:** `CLAUDE.md` places the plug-ins beside `iConsole`, selected at start-up by the composition root; the edge lets `iTerminal.Core` (and its tests) use Windows console internals, and puts the plug-in into every project that references `iTerminal.Core`. Principle: dependency direction.
    - **Improvement:** remove the `ProjectReference`.
    - **Effort:** S.
    - **Risk of the change:** none expected; the build shows any hidden use, and `iTerminal.Test` (which references `iTerminal.Core` only) shows whether the end-to-end host relied on it.

11. **Patch and minor updates are waiting** — **Low**, kind: dependency.
    - **Where:** `dotnet list --outdated`: `coverlet.collector` 10.1.0 to 10.1.1 (4 test projects), `Microsoft.Extensions.AI` and `.OpenAI` 11.0.0 to 11.0.1, `SSH.NET` 2026.0.0 to 2026.0.1, `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.156.0 to 4.157.0, `OpenMcdf` 3.3.0 to 3.4.0, `Tomlyn` 2.2.0 to 2.11.1.
    - **Problem:** none are security fixes as far as the advisory data shows; left alone, they turn into a large jump later.
    - **Improvement:** one update pull request for these, after finding 5 so each is one line. `Tomlyn` crosses nine minor versions; its one user is [iConsole/Toml/TomlDocumentReader.cs:18](../../iConsole/Toml/TomlDocumentReader.cs) (`TomlSerializer.Deserialize<TomlTable>`).
    - **Effort:** S.
    - **Risk of the change:** build, `iConsole.Test` (TOML and OLE fixture tests), `iTerminal.Core.Test` (AI and SSH tests), and the end-to-end suite for the SkiaSharp natives.

12. **Moving to Playwright 1.65 makes every user download the driver again** — **Low**, kind: dependency.
    - **Where:** [iTerminal.Core/iTerminal.Core.csproj:31](../../iTerminal.Core/iTerminal.Core.csproj) and [iTerminal.Core.Test/iTerminal.Core.Test.csproj:26](../../iTerminal.Core.Test/iTerminal.Core.Test.csproj), 1.64.1 to 1.65.1; the line rule in [runtime-dependencies.md:6](../architecture/runtime-dependencies.md). Users: `Jobs/OnlineQuoteJob.cs` and `PlaywrightRuntimeDependency.cs`.
    - **Problem:** the driver is tied to the library's major.minor, so the update is a new line: each install fetches the ~210 MB package and Chromium again at the next start-up offer. That is a cost to users, not a defect.
    - **Improvement:** update both references together (one line once finding 5 is done) and bundle it with another release-worthy change rather than alone.
    - **Effort:** S.
    - **Risk of the change:** `PlaywrightRuntimeDependencyTest` and `NuGetPackageFeedTest` cover the version logic; `OnlineQuoteJobTest` the use. A real install check needs the network and was not part of this review.

13. **YamlDotNet is two major versions behind** — **Low**, kind: dependency.
    - **Where:** [iConsole/iConsole.csproj:29](../../iConsole/iConsole.csproj), 16.3.0 to 18.1.0. The only user is [iConsole/Yaml/YamlDocumentReader.cs:21](../../iConsole/Yaml/YamlDocumentReader.cs) (`YamlStream`, `YamlDotNet.RepresentationModel`).
    - **Problem:** a reader of untrusted files on an old major line; fixes land on the new one. The representation-model API is the part least likely to have changed, but that was not checked: **unverified**.
    - **Improvement:** update to 18.1.0 on its own pull request.
    - **Effort:** S.
    - **Risk of the change:** source breaks show in the build; `iConsole.Test` has the YAML reader tests and the data-tree viewer tests.

14. **The SDK floats, and a compiler feature is switched on for every project without a note in the architecture pages** — **Low**, kind: dependency, documentation.
    - **Where:** no `global.json`; both workflows install `11.0.x` ([.github/workflows/build.yml:32](../../.github/workflows/build.yml) and `:110`); [Directory.Build.props:8](../../Directory.Build.props) (`runtime-async=on`, added in #505).
    - **Problem:** a new SDK feature band changes analyzers and the compiler under the same commit, so a red build on `main` can come from the runner image, not the code. `runtime-async` changes how every async method runs (stack traces, profiler output, the `Job` and `TaskManager` behaviour the threading rules rely on), yet no page in `Documentation/architecture/` mentions it. Whether SDK 11.0.100 still needs the switch or has made it the default was not checked: **unverified**.
    - **Improvement:** add a `global.json` with `"version": "11.0.100"` and `"rollForward": "latestPatch"`, and one paragraph in `ui-framework.md` (threading) that says runtime async is on, why, and how to turn it off for a diagnosis.
    - **Effort:** S.
    - **Risk of the change:** a machine without that feature band fails to build until it installs it; the error says which version.

15. **The packaging files and pages disagree on the default version, the licence and arm64** — **Low**, kind: documentation.
    - **Where:** [Directory.Build.props:4](../../Directory.Build.props) (default `0.0.0.0`), [iTerminal.Installer/iTerminal.Installer.wixproj:22](../../iTerminal.Installer/iTerminal.Installer.wixproj) and [iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj:27](../../iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj) (default `1.0.0`), [build-and-packaging.md:10](../architecture/build-and-packaging.md) ("default `1.0.0`"), `:12` ("the repository has no licence text", see finding 2), `:35` ("`linux-arm64` is mapped but has not been built here", while [.github/workflows/release.yml:82](../../.github/workflows/release.yml) builds it for every release).
    - **Problem:** three defaults for one number. The WiX SDK imports `Directory.Build.props` before the `.wixproj` sets its own default, so the `1.0.0` there is probably dead and a local MSI is `0.0.0.0` (**unverified**, no Windows build was run). Releases always pass `-p:Version`, so only local builds and readers are affected.
    - **Improvement:** keep the default in `Directory.Build.props` only, drop the two others (the Linux `.proj` is not an SDK project and needs its own, so give it the same `0.0.0.0`), and correct the three sentences in `build-and-packaging.md`.
    - **Effort:** S.
    - **Risk of the change:** a local `.deb` without `-p:Version` gets version `0.0.0.0`; dpkg accepts it.

16. **Tar and zip are read by two libraries, with three different "stays inside the folder" checks** — **Low**, kind: consistency.
    - **Where:** SharpZipLib (`Tar`, `Zip`, `GZip`, `BZip2`) in `Services/Archives/ArchiveExtractionService.cs`, `ArchiveDriveService.cs` and `ManagedArchiveWriter.cs`; the BCL (`System.Formats.Tar`, `ZipArchive`) in `Services/Updates/UpdateService.cs`, `Services/RuntimeDependencies/ReleaseArchive.cs` and `Services/Docker/DockerDriveService.cs`. The checks: [ArchiveExtractionService.cs:156](../../iTerminal.Core/Services/Archives/ArchiveExtractionService.cs) (case-insensitive on Windows only), [ReleaseArchive.cs:100](../../iTerminal.Core/Services/RuntimeDependencies/ReleaseArchive.cs) (always ordinal), [UpdateService.cs:604](../../iTerminal.Core/Services/Updates/UpdateService.cs) (always case-insensitive, also on Linux).
    - **Problem:** SharpZipLib has a stated reason (writing encrypted ZIP, [file-operations.md:210](../architecture/file-operations.md)); the split for reading is not explained. The path check that guards against escaping entries is written three times with three case rules; the update one is looser than needed on Linux, where a folder differing only in case is a different folder. No exploit was found (the roots end in a separator), but a fourth extractor would copy one of the three at random. Principle: one solution per problem.
    - **Improvement:** one `internal static string? ArchivePaths.Inside(string root, string entryName)` in `iTerminal.Core/Helper` with the `ArchiveExtractionService` rule (rooted and drive-letter names refused, case-insensitive only on Windows), used by the three; keep both libraries.
    - **Effort:** S.
    - **Risk of the change:** the extraction tests in `iTerminal.Core.Test` (archive, release-archive and update tests) cover the escape cases and would catch a changed rule.

**Package table**

"Used by" is the number of source files that use the package's namespace in non-test projects, then in test projects; "native" means the package carries native files only. "Latest" is the latest stable version on nuget.org on 2026-11-21 from `dotnet list --outdated`; a pre-release is named only where it matters.

| Package | Project | Current | Latest | Major change | Used by (count) | Action |
| --- | --- | --- | --- | --- | --- | --- |
| HtmlAgilityPack | iConsole | 1.13.1 | 1.13.1 | No | 2 / 0 | Keep |
| Iced | iConsole | 1.22.0 | 1.22.0 | No | 1 / 0 | Keep (finding 8) |
| JetDatabaseReader | iConsole | 3.0.0 | 3.0.0 | No | 1 / 0 | Keep (finding 8) |
| DocumentFormat.OpenXml | iConsole | 3.5.1 | 3.5.1 | No | 2 / 3 | Keep (finding 8) |
| Microsoft.Extensions.Hosting | iConsole | 11.0.0 | 11.0.0 | No | 2 / 0 | Keep (the host builder) |
| Microsoft.FeatureManagement | iConsole | 4.8.0 | 4.8.0 | No | 0 / 0 | Remove (finding 9) |
| OpenMcdf | iConsole | 3.3.0 | 3.4.0 | No | 2 / 1 | Update (finding 11) |
| PdfPig | iConsole | 0.1.16 | 0.1.16 | No | 1 / 0 | Keep |
| SkiaSharp | iConsole | 4.156.0 | 4.157.0 | No | 2 + 9 in Core / 9 | Update (finding 11) |
| SkiaSharp.NativeAssets.Linux.NoDependencies | iConsole | 4.156.0 | 4.157.0 | No | native | Update with SkiaSharp |
| Svg.Skia | iConsole | 3.0.0 | 5.2.3 | Yes | 1 / 1 | Update, one owner (finding 1) |
| System.Security.Cryptography.Pkcs | iConsole | 11.0.0 | 11.0.0 | No | 1 + 1 in Core / 3 | Keep |
| Tomlyn | iConsole | 2.2.0 | 2.11.1 | No | 1 / 1 | Update (finding 11) |
| YamlDotNet | iConsole | 16.3.0 | 18.1.0 | Yes | 1 / 1 | Update alone (finding 13) |
| FluentFTP | iTerminal.Core | 55.1.0 | 55.1.0 | No | 1 / 1 | Keep |
| Microsoft.AspNetCore.DataProtection | iTerminal.Core | 11.0.0 | 11.0.0 | No | 1 / 1 | Optional drop (brought by .Extensions) |
| Microsoft.AspNetCore.DataProtection.Extensions | iTerminal.Core | 11.0.0 | 11.0.0 | No | 1 / 1 | Keep |
| Microsoft.EntityFrameworkCore | iTerminal.Core | 11.0.0 | 11.0.0 | No | 16 / 19 | Keep |
| Microsoft.EntityFrameworkCore.Sqlite | iTerminal.Core | 11.0.0 | 11.0.0 | No | provider of EF | Keep |
| Microsoft.Data.Sqlite | iTerminal.Core | 11.0.0 | 11.0.0 | No | 6 / 6 | Keep (used directly) |
| Microsoft.Extensions.AI | iTerminal.Core | 11.0.0 | 11.0.1 | No | 8 / 6 | Update (finding 11) |
| Microsoft.Extensions.AI.OpenAI | iTerminal.Core | 11.0.0 | 11.0.1 | No | 6 / 2 | Update (finding 11) |
| imobiledevice-net | iTerminal.Core | 1.3.17 | 1.3.17 (deprecated) | No | 2 / 0 | Decide platform contract (finding 4) |
| Microsoft.Mxc.Sdk | iTerminal.Core | 1.1.0 | 1.1.0 | No | 1 / 1 | Keep; the class comment still calls it "a preview" |
| Microsoft.Playwright | iTerminal.Core | 1.64.1 | 1.65.1 | No (new line) | 2 / 3 | Update with a release (finding 12) |
| MonoTorrent | iTerminal.Core | 3.0.2 | 3.0.2 | No | 1 / 0 | Keep |
| NLayer | iTerminal.Core | 3.0.1 | 3.0.1 | No | 1 / 0 | Keep |
| PureHDF | iTerminal.Core | 2.2.0 | 2.2.0 (3.0.0-beta.1 out) | No | 1 / 1 | Keep; watch 3.0 |
| Parquet.Net | iTerminal.Core | 6.1.0 | 6.1.0 | No | 1 / 2 | Keep |
| Serilog | iTerminal.Core | 4.4.0 | 4.4.0 | No | 39 / 22 | Keep |
| Serilog.Sinks.File | iTerminal.Core | 8.0.0-dev-02318 | stable 7.x (unverified) | Yes (to stable) | 1 / 0 | Move to stable (finding 3) |
| SharpZipLib | iTerminal.Core | 1.4.2 | 1.4.2 | No | 3 / 7 | Keep (finding 16) |
| SMBLibrary | iTerminal.Core | 1.5.8.1 | 1.5.8.1 | No | 1 / 1 | Keep; LGPL notice (finding 2) |
| SSH.NET | iTerminal.Core | 2026.0.0 | 2026.0.1 | No | 1 / 0 | Update (finding 11) |
| Svg.Skia | iTerminal.Core | 5.2.3 | 5.2.3 | No | 1 / 1 | Remove here once iConsole owns it (finding 1) |
| Whisper.net | iTerminal.Core | 1.9.1 | 1.9.1 | No | 1 / 0 | Keep |
| Whisper.net.Runtime | iTerminal.Core | 1.9.1 | 1.9.1 | No | native | Keep |
| ZstdSharp.Port | iTerminal.Core | 0.8.8 | 0.8.8 | No | 3 / 0 | Keep (the .NET 11.0.0 reference pack has no zstd type) |
| Packaging.Targets | iTerminal (Linux packaging only) | 0.1.232 | unverified | Unknown | build only | Check by hand |
| WixToolset.Sdk, .UI.wixext, .Util.wixext | iTerminal.Installer | 5.0.2 | unverified | Unknown | build only | Check by hand; one version property (finding 5) |
| MSTest.TestAdapter / MSTest.TestFramework | 7 test projects | 4.5.3 | 4.5.3 | No | all tests | Keep; centralize (finding 5) |
| Microsoft.NET.Test.Sdk | 6 test projects | 18.12.0 | 18.12.0 | No | all tests | Keep; centralize (finding 5) |
| coverlet.collector | 4 test projects | 10.1.0 | 10.1.1 | No | coverage | Update (finding 11) |
| Moq | iConsole.Test, iTerminal.Core.Test | 4.21.0 | 4.21.0 | No | 0 / 531 | Keep |
| ZXing.Net | iConsole.Test | 0.16.11 | 0.16.11 | No | 0 / 1 | Keep |
| Reqnroll.MSTest | iTerminal.Specifications.Test | 3.3.4 | 3.3.4 | No | 0 / 215 | Keep |
| Microsoft.Playwright | iTerminal.Core.Test | 1.64.1 | 1.65.1 | No (new line) | 0 / 3 | Same version as Core (findings 5, 12) |

**What works well**

- The Playwright library is shipped and its 130 MB driver is not: `ExcludeAssets="build;buildTransitive"` with the reason next to it ([iTerminal.Core/iTerminal.Core.csproj:26](../../iTerminal.Core/iTerminal.Core.csproj)), and the driver is installed for the exact library version from the NuGet package, checked against the SHA-512 of its catalog entry.
- The Whisper model is pinned by commit URL, size and SHA-256 and written through `VerifiedDownload` (`.partial`, check, rename), and every GitHub download refuses an asset without a published digest ([GitHubReleaseFeed.cs:45](../../iTerminal.Core/Services/RuntimeDependencies/GitHubReleaseFeed.cs)).
- Non-free and large tools (rar, unrar, 7-Zip, ffmpeg, git, yt-dlp) are left to the system package manager and never bundled; an existing install is used as it is and never overwritten.
- `Packaging.Targets` is referenced only when `LinuxPackaging` is `true`, so ordinary builds never restore it, and the Debian and RPM dependency lists are owned by the project with the reason written down ([iTerminal/iTerminal.csproj:36](../../iTerminal/iTerminal.csproj) to `:80`).
- Every project targets the same framework, `net11.0`, and the Microsoft packages that move with the runtime are all on 11.0.0.
- No package is vulnerable, and package comments explain the non-obvious choices (ZstdSharp because the reference pack has no zstd, which was confirmed against the installed 11.0.0 reference pack; SharpZipLib for encrypted ZIP; Whisper natives shipped because they are small).

**Research cross-reference**

- Security overview, item 27 (TUF signed metadata for self-update): confirmed, and finding 7 extends the same weakness to the Ollama and PowerShell downloads, which take the digest from the same GitHub release. A pinned hash in code (the Whisper pattern) closes it for those two without TUF, so finding 7 can go first.
- Security overview, inventory item 4 (CycloneDX SBOM reader) and the writer in `DotnetSolutionService.GenerateSbom`: the writer is a possible source for the notice file of finding 2, and an SBOM of iTerminal's own release would be a natural by-product.
- Security overview, "security-tools 3.6" (OSV package check): finding 6 is the same check for iTerminal's own build; it needs no new code, only restore settings.
- AI overview, item 2 (in-process model, "large native packages"): needs an architecture decision on where native-heavy packages live before adoption; finding 8 proposes the place.
- Profiling overview, finding 4 (start-up 5.4 to 6.0 s): does not measure assembly loading, so the start-up effect of finding 8 stays unverified; a `StartupTrace` mark around the first use of a format reader would answer it.

**Suggested order**

1. Quick wins, no dependencies: finding 9 (remove `Microsoft.FeatureManagement`), finding 10 (remove the unused project edge), finding 3 (stable log sink), finding 14 (`global.json`, runtime-async note), finding 15 (version defaults and page corrections).
2. Finding 1 (one `Svg.Skia` version), the only High finding. It does not wait for finding 8: if `iConsole.Formats` is made later, the reference moves with the file.
3. Finding 5 (Central Package Management), then finding 6 (audit, lock files, source mapping), which is simpler once versions are central.
4. Finding 11 (patch updates), then finding 13 (YamlDotNet) and finding 12 (Playwright, with a release), all after finding 5 so each is one line.
5. Finding 2 (third-party notices and the project licence in the packages), before the next public release.
6. Finding 4 (iPhone drive platform contract) and finding 7 (pinned runtime downloads), which need a decision from the maintainer first.
7. Finding 16 (one archive path check), and finding 8 (`iConsole.Formats`), the largest change, last.

## Resolved

None yet.
