# Security features overview for iTerminal

This document collects security features and tools that could suit iTerminal, a dual-pane file manager and terminal. It is a research record, not a plan. An item marked **Proposed** is unbuilt. Each survey is appended at the end, and older survey text is not rewritten. The prior record of what iTerminal already has, and of the features proposed before this document, is `Documentation/security-tools.md`. This document does not repeat it; it cites its sections as "security-tools N.N".

## What iTerminal already has

The capabilities in security-tools.md section 2 were checked by file existence on branch `docs/research-overviews` at commit `41d889b2` on 2026-11-20. Every path below exists, and no status change was found.

- Checksums and hash manifests: `iTerminal.Core/Services/Checksums/ChecksumService.cs`, `iTerminal.Core/Dialogs/HashManifestDialog.cs`.
- Publisher signatures: `iTerminal.Core/Services/Signing/SignatureService.cs`.
- File encryption: `iTerminal.Core/Services/Encryption/FileEncryptionService.cs`.
- Hash reputation: `iTerminal.Core/Services/Reputation/HashReputation.cs`.
- Malware scan: `iTerminal.Core/Services/Defender/DefenderScanService.cs`.
- Secure wipe: `iTerminal.Core/Services/Wipe/SecureWipeService.cs`.
- Password manager and host trust: `iTerminal.Core/Services/PasswordManager.cs`, `iTerminal.Core/Services/TrustedHostStore.cs`.
- Executable analysis: `iTerminal.Core/Services/Executables/` (PE, ELF and Mach-O analysers).
- Alternate data streams: `iTerminal.Core/Services/Streams/StreamAttributeService.cs`.
- Elevation: `iTerminal.Core/Services/Elevation/ElevationService.cs`.
- Certificates and hex view: `iConsole/Certificates/`, `iConsole/Controls/HexViewer.cs`.
- Duplicates and file disguise: `iTerminal.Core/Services/Duplicates/`, `iTerminal.Core/Services/Security/FileDisguiseChecker.cs`.
- Metadata strip and binary diff: `iTerminal.Core/Dialogs/StripMetadataDialog.cs`, `iTerminal.Core/Dialogs/BinaryDiffView.cs`.
- AI panel tools: `iTerminal.Core/AI/AiPanelTools.cs`.

## Proposed features

| Feature | Seen in | Status in iTerminal | Fits |
| --- | --- | --- | --- |
| Shortcut (.lnk) inspector | MS-SHLLINK | Missing | `iConsole` parser, F3 viewer |
| CycloneDX SBOM reader | CycloneDX 1.7 | Missing | `iConsole` parser, F3 viewer |
| BLAKE3 digest | BLAKE3 | Missing | `ChecksumService` |
| ML-DSA signature in the `.sig` format | FIPS 204 (MLDsa class) | Missing | `SignatureService` |
| Argon2id key derivation for `.enc` files | RFC 9106 | Missing | `FileEncryptionService` |
| C2PA content credentials viewer | c2pa-rs | Missing | Viewer, `iConsole` or `iTerminal.Core/Services` |
| RFC 3161 timestamp check for evidence | RFC 3161 | Missing | Evidence bundle (security-tools 3.1) |
| Sigma rules over event logs | Sigma specification | Missing | Rule evaluator in `iConsole`, fed by security-tools 3.7 |

## Suggested order

Ranked by value to iTerminal against effort. The reasons are in the survey below.

1. Shortcut (.lnk) inspector (item 5).
2. CycloneDX SBOM reader (item 4).
3. BLAKE3 digest (item 1).
4. ML-DSA signatures (item 3).
5. Argon2id key derivation for `.enc` files (item 2).
6. C2PA content credentials viewer (item 6).
7. RFC 3161 timestamp check (item 7).
8. Sigma rules over event logs (item 8).

### Surveys

#### Survey 1: hash functions, key derivation, signatures, shortcut files, provenance, timestamps and event-log rules

Date 2026-11-20. Basis: official documentation (a standard or specification, a project's repository and release page, and vendor documentation), not run-time behaviour. Where a fact came only from a search snippet, or a page did not show a year, the item says so. "Not found" means a case-insensitive grep of `iConsole/` and `iTerminal.Core/` (`*.cs`) on branch `docs/research-overviews` at commit `41d889b2`, with the terms listed in the item.

**Integrity**

1. **BLAKE3 digest.** Missing: `ChecksumAlgorithm` has MD5, SHA-1, SHA-256 and SHA-512 only. Fits: `ChecksumService` (the enum, `Algorithms`, `NameOf`), and the algorithm lists in `ChecksumDialog` and `HashManifestDialog`. **Seen in:** the BLAKE3 README, which names a public-domain (CC0 1.0) or Apache-2.0 licence and points to the design paper and the C2SP specification ([BLAKE3](https://github.com/BLAKE3-team/BLAKE3)); the latest release is 1.8.7, dated 20 August with no year on the fetched page, so the year is unverified ([releases](https://github.com/BLAKE3-team/BLAKE3/releases)). **Not found:** `blake`, `blake3`. `ChecksumAlgorithm` and `IncrementalHash` are present, as the Fits line shows. The .NET 11 namespace has no BLAKE3 type, so a package or a port is needed, which is the maintainer's choice. A BLAKE3 manifest would not verify with `sha256sum`, so the manifest format needs its own algorithm name.

**Encryption**

2. **Argon2id key derivation for `.enc` files.** Missing: the file format has one key derivation id (`KdfPbkdf2Sha256 = 1`), and the reader refuses any other id as unsupported. Fits: `FileEncryptionService` (header byte 6, and the format table in file-operations.md). A header that names a memory cost would need the same bounds as the iteration count (`MinIterations`, `MaxIterations`), or a hostile file could exhaust memory. **Seen in:** RFC 9106 (Informational, September 2021), which recommends Argon2id when the threat model is unknown, gives two parameter sets with four lanes (one pass with 2 GiB, or three passes with 64 MiB), and recommends a 128-bit salt ([RFC 9106](https://www.rfc-editor.org/rfc/rfc9106)). The .NET 11 namespace lists `Rfc2898DeriveBytes` and no Argon2 type, so a package is needed ([System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography?view=net-11.0)). Konscious.Security.Cryptography.Argon2 1.3.1 is MIT-licensed and implements Argon2d, Argon2i and Argon2id; its last release is dated 19 June 2024, more than a year before this survey, so its upkeep must be checked before adoption ([NuGet](https://www.nuget.org/packages/Konscious.Security.Cryptography.Argon2)). A NuGet package is shipped, not downloaded, so it does not follow the runtime-dependency pattern. **Not found:** `argon`, `scrypt`. `kdf` and `pbkdf2` match `FileEncryptionService`, which uses PBKDF2 only.

**Signing**

3. **ML-DSA signatures.** Missing: the in-process `.sig` format (`iTerminal-signature-v1 <rsa|ecdsa> <base64>`) knows two algorithm names; any other name is reported as "could not check" by `SignatureService`. Fits: `SignatureService` (`PemPrefix`, `VerifyRsa`, `VerifyEcdsa`, the algorithm choice in `SignPemAsync`), and the verdict lines of `SignatureDialog`. **Seen in:** the MLDsa class reference, which states that the algorithm is specified by FIPS 204, that `IsSupported` depends on the platform, and that the class imports and exports PEM keys, signs and verifies data ([MLDsa](https://learn.microsoft.com/dotnet/api/system.security.cryptography.mldsa)). The fetched page shows no experimental note. A search snippet says .NET 10 marks the class experimental under SYSLIB5006; this is unverified. **Not found:** `mldsa`, `ml-dsa`, `fips 204`, `post-quantum`, `pqc`, `mlkem`, `slhdsa`. A verifier must still refuse a signature made with another algorithm, as the existing PEM method already does.

**Inventory**

4. **CycloneDX SBOM reader.** Missing: nothing reads `bom.json`, `*.cdx.json` or `bom.xml`. Fits: a parser in `iConsole` for the JSON and XML forms, an F3 viewer for those file names, and the component list as an input to the package check in security-tools 3.6. **Seen in:** the CycloneDX overview, which names JSON, XML and Protocol Buffers as its formats and gives version 1.7 ([overview](https://cyclonedx.org/specification/overview/)); version 1.7 was released on 21 October 2025 ([announcement](https://cyclonedx.org/news/cyclonedx-v1.7-released)); the latest tag on the specification repository is 1.7.2, dated 17 September with no year on the fetched page, so the year is unverified ([releases](https://github.com/CycloneDX/specification/releases/latest)). The fetched pages name no licence for the specification (unverified). SPDX was not checked. **Not found:** `cyclonedx`, `sbom`, `spdx`, `purl`, `.cdx`.

**Triage**

5. **Windows shortcut (.lnk) inspector.** Missing: there is no shell-link reader. `.lnk` appears only in the Windows run-as list (`ElevationService.WindowsRunAsExtensions`). Enter on a `.lnk` file goes to the system's default program (file-operations.md), so the target of a shortcut is seen only by running it. Fits: a binary parser in `iConsole` (header, link target ID list, link info and string data), an F3 viewer, and an info-strip line. The parser reads the target, arguments and working folder as text and never resolves, follows or launches them. **Seen in:** MS-SHLLINK, the Shell Link Binary File Format, last updated 21 November 2025 ([introduction](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/99e8d0e5-5bc6-4aed-af37-da7f584f832a)); its structures are a header, an optional target ID list, optional link info, optional string data and extra data ([structures](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/747629b3-b5be-452a-8101-b9a2ec49978c)); the string data holds a name, a relative path, a working folder, command-line arguments and an icon location ([StringData](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/17b69472-0f34-4bcf-b290-eccdb8de224b)). **Not found:** `shelllink`, `IShellLink`. The term `.lnk` matches only the run-as list.

**Provenance**

6. **C2PA content credentials viewer.** Missing: there is no manifest reader. The word "manifest" in the code refers to hash manifests only. Fits: a read-only viewer for the image types the reader supports (the README does not list them, so check before building), placed in `iConsole` if a managed reader exists, or in `iTerminal.Core/Services` for a native one. The reference implementation is Rust. A .NET build would call its C interface, which is a native library: shipped, or installed on demand under the runtime-dependency pattern if downloaded. That choice is the maintainer's. A verdict must say that a signature is valid for its issuer; whether to trust the issuer is the user's decision. **Seen in:** the c2pa-rs README, which is dual-licensed MIT or Apache-2.0, names version 2.4 of the C2PA specification, and says the library reads and validates manifests as well as signing them ([c2pa-rs](https://github.com/contentauth/c2pa-rs)); the most recent release on the page is the C interface, c2pa-c-ffi v0.92.2, published 16 November (2026, per the release notes on the page) ([releases](https://github.com/contentauth/c2pa-rs/releases)). A Rust crate version of 0.90.17 came from a search snippet and is unverified. **Not found:** `c2pa`, `jumbf`, `content credential`, `provenance`.

**Auditing**

7. **RFC 3161 timestamp check for evidence.** Missing: there is no time-stamp request and no token check. In the code, "timestamp" means a file, quote or log time. Fits: the evidence bundle in security-tools 3.1, as an optional step that checks a token against the bundle's manifest. Parsing a token needs the ASN.1 reader in security-tools 3.5. A request sends data to a time-stamp authority, so it is opt-in and the dialog says what is sent (security-tools section 5, rule 8). **Seen in:** RFC 3161, a Proposed Standard from August 2001 updated by RFC 5816, which defines how a client asks a time-stamp authority for a token and how the authority answers ([RFC 3161](https://www.rfc-editor.org/rfc/rfc3161)). A search snippet says the authority receives a hash and not the file; this is unverified against the RFC text. **Not found:** `rfc 3161`, `time-stamp authority`, `tsa`. The `timestamp` matches are file, quote or log times.

**Detection**

8. **Sigma rules over event logs.** Missing: there is no event-log reader and no rule matcher. Fits: a rule evaluator in `iConsole` (field matching, modifiers and the `condition` expression), fed by the Windows event log viewer and the authentication log summary in security-tools 3.7. It runs as a background task, and a rule hit is shown as a hint with its rule name, never as a verdict (security-tools section 5, rule 9). **Seen in:** the sigma-specification repository, which holds the rules specification (`specification/sigma-rules-specification.md`), a JSON schema for rules, and appendices for modifiers, tags and the taxonomy of field names and log sources ([sigma-specification](https://github.com/SigmaHQ/sigma-specification)); the latest release is v2.1.0, dated 12 September with no year on the fetched page, so the year is unverified ([releases](https://github.com/SigmaHQ/sigma-specification/releases)). The fetched pages name no licence for the specification or the rules; a search snippet says the specification is public domain and the rules are under DRL 1.1 (unverified). **Not found:** `sigma`, `EventLog`, `EventRecord`, `winevt`, `journalctl`, `auth.log`. Four test files match the broader pattern through paths such as `/var/log`; none is a reader of an event log.

**Suggested order (survey 1 items)**

1. Shortcut (.lnk) inspector (item 5): a pure parser, reachable from F3, and it shows a target without running it.
2. CycloneDX SBOM reader (item 4): a pure parser, and its component list feeds the package check in security-tools 3.6.
3. BLAKE3 digest (item 1): a small change to the checksum enum, once a maintained implementation is chosen.
4. ML-DSA signatures (item 3): one more algorithm in the in-process format, with the platform API already in .NET 11.
5. Argon2id key derivation (item 2): needs a package and bounds in the header.
6. C2PA content credentials viewer (item 6): needs a native-library decision.
7. RFC 3161 timestamp check (item 7): needs an opt-in network call and the ASN.1 reader.
8. Sigma rules over event logs (item 8): depends on the event-log reader in security-tools 3.7.

**Already covered (no new item)**

- YARA rule scanning: security-tools 3.2. A newer YARA implementation was not checked.
- ClamAV behind the malware-scan interface: security-tools 3.2.
- Package vulnerability check through OSV: security-tools 3.6.
- age interop: security-tools section 4 and 3.4.
- TOTP, breached-password check, SSH key inspector, JWT decoder, secret scanner: security-tools 3.4.
- Argon2 verification of stored hashes: security-tools 3.5. Item 2 is a different use, a key for files, and is not a duplicate.
- Windows event log viewer and authentication log summary: security-tools 3.7. Items 8 depends on them.
- TLS endpoint inspector and certificate expiry watch: security-tools 3.3.
- Publisher signatures with sigstore `.bundle` files: security-tools section 2 (Have). Item 6 is a different format.
- Hash manifests: security-tools section 2 (Have).

**Unverified in this survey**

- Item 1: the year of BLAKE3 release 1.8.7.
- Item 3: the experimental status of the MLDsa class in .NET 10.
- Item 4: the year of CycloneDX tag 1.7.2 and the licence of the specification.
- Item 6: the Rust crate version and the image types the reader supports.
- Item 7: whether the time-stamp authority receives a hash only.
- Item 8: the year of Sigma specification v2.1.0, and the licences of the specification and the rules.

#### Survey 2: attestations, git and PDF signatures, look-alike names, mail authentication results, image scanning, application control, keystores and known-file sets

Date 2026-11-20. Basis: official documentation (a standard or specification, a project's repository and release page, and vendor documentation), not run-time behaviour. Where a fact came only from a search snippet, or a page showed no year or no licence, the item says so. The status of items 1 to 8 was re-checked on branch `docs/research-security-survey-2` at commit `fcc5640e`: the checksum algorithms, the `.enc` key id and iteration bounds, the `.sig` format, the `.lnk` entry in the run-as list, and the absence of the BLAKE3, Argon2, CycloneDX, C2PA, RFC 3161, Sigma and shortcut terms all still match, so no status changed and no dated status line is needed. "Not found" means a case-insensitive grep of `iConsole/` and `iTerminal.Core/` (`*.cs`) on that branch and commit, with the terms listed in the item.

**Signatures and provenance**

9. **in-toto attestation check.** Missing: nothing reads an attestation (a `Statement` with its `subject` digests and `predicateType`), and no subject digest is compared with a file. Fits: a pure JSON reader in `iConsole` for the statement, a subject check in `iTerminal.Core/Services/Checksums` that uses `ChecksumService` for the file's SHA-256, and an F3 viewer that recognises a statement by its `_type` field rather than by file name. A matching digest says the file is the one the statement names, and nothing about whether the builder is trusted; the verdict has to say so (security-tools section 5, rule 9). Checking the signature on the envelope is a separate step that needs a decision on the verifier. **Seen in:** the in-toto Attestation Framework repository, which describes a format specification with maintainer-vetted predicates, says the project is still under development, and names language bindings for Go, Python, Rust and Java ([in-toto/attestation](https://github.com/in-toto/attestation)). Its latest release is v1.2.0, marked Latest, dated 18 March with no year on the fetched page ([releases](https://github.com/in-toto/attestation/releases)); the release notes for v1.1.0 give the statement type `https://in-toto.io/Statement/v1`. The repository page names no licence (unverified). A search snippet says SLSA provenance is one of the in-toto predicate types ([SLSA blog](https://slsa.dev/blog/2023/05/in-toto-and-slsa)); this is unverified. **Not found:** `in-toto`, `intoto`, `slsa`, `attestation`, `provenance`. `cosign` and `sigstore` match only the publisher-signature code, which reports sigstore files as not checked.

10. **Git commit and tag signature status.** Missing: the history of the `git://` drive reads `%H`, `%h`, `%ct` and `%s` (`iTerminal.Core/Services/Git/GitDriveService.cs`, the `log` format near line 142; the HEAD line near line 94 reads `%H`, `%ct` and `%s`), and the `GitCommit` record of the git client's log holds a short hash, subject, author and relative date with no signature field (`iTerminal.Core/Model/Git/GitCommit.cs`). A signed commit therefore looks like any other. Fits: the format string in `GitDriveService` (adding `%G?` and `%GS`), a field on `iTerminal.Core/Model/Git/GitCommit.cs`, and the history listing of the `git://` drive. A letter `G` says the signature checks against a key in the user's keyring; it does not say the key is trusted, which is the rule the publisher-signature check already follows. **Seen in:** the git pretty-formats manual, which documents Git 2.54.0 and says the text is unchanged through 2.56.1. `%G?` prints one letter: G good, B bad, U good with unknown validity, X good but expired, Y good made by an expired key, R good made by a revoked key, E cannot be checked, N no signature. `%GS` prints the signer's name, `%GK` the signing key, `%GF` its fingerprint, `%GT` the trust level and `%GG` the raw output of the verifier ([pretty-formats](https://git-scm.com/docs/pretty-formats)). **Unverified:** whether git runs the verifier once for each signed commit (a cost on a 100-commit listing), and whether SSH-signed commits give the same letters. **Not found:** `show-signature`, `%G?`, `verify-commit`, `verify-tag`, `gitsign`, `gpgsig`, `commit signature`, `signature status`.

11. **PDF signature inspector.** Missing: nothing in `iConsole/` or `iTerminal.Core/` reads a PDF signature dictionary, its byte range or its signer; the PDF risk indicators in security-tools 3.2 read other entries. Fits: a pure reader in `iConsole` that finds the signature dictionaries in the object list, shown in F3 for a PDF and in Properties. The first step only lists what is there. Cryptographic validation needs a CMS parser and a decision about trust, and is not part of this item. A listed signature is a fact about the file, never a verdict that it is valid. **Seen in:** ETSI EN 319 142-1, the PAdES part 1 (baseline), which the search results give as version 1.2.1 dated 2024. The fetched PDF could not be read as text, so every PAdES detail here is unverified ([ETSI EN 319 142-1](https://www.etsi.org/deliver/etsi_en/319100_319199/31914201/01.02.01_60/en_31914201v010201p.pdf)). A search snippet says the signature dictionary holds `Filter`, `SubFilter`, `ByteRange` and `Contents`, and that the byte range covers the file except the signature value (unverified). The Adobe copy of the PDF 1.7 reference was over the fetch size limit and was not read. **Not found:** `/ByteRange`, `/Sig`, `pades`, `xades`, `PdfSignature`, `digital signature`. The one `pades` hit is inside the word "Spades" in a game.

**Names and mail**

12. **Look-alike file names.** Partly present: `iConsole/Security/FileDisguise.cs` already finds bidi overrides and other bidi controls, zero-width and other control characters in a name (`DisguiseFindings.HiddenCharacters`), a double extension and a type mismatch; `FileDisguise.Mask` shows the names on the panel, in Properties and in the info strip. Missing: no check for characters that look alike across scripts (a Latin letter beside a Cyrillic one, for example), and no check for a name that mixes scripts. Fits: a pure function beside `FileDisguise.AnalyzeName` in `iConsole/Security`, a new `DisguiseFindings` flag, and a line in `FileDisguiseMessage`. Real names are often mixed-script, so the result is a hint with its basis, never a verdict. **Seen in:** UTS #39 (Unicode Security Mechanisms), revision 34 (Version 18.0.0), dated 27 August 2026. Section 4 covers whole-script confusables (4.1) and mixed-script confusables (4.2), section 5.1 covers mixed-script detection, and the confusable mappings are published as a data file, `confusables.txt` ([UTS #39](https://www.unicode.org/reports/tr39/)). The licence of the data file was not checked (unverified). **Not found:** `confusable`, `homoglyph`, `homograph`, `mixed script`, `UnicodeScript`, `IdnMapping`, `Punycode`, `skeleton` (the only hit is the loading-placeholder control `Skeleton`). `NormalizationForm` is used only by the encryption passphrase.

13. **Authentication-Results reader for `.eml` messages.** Partly present: `iConsole/Services/MimeMessageReader.cs` reads an e-mail's addresses, date, subject, body and attachment names, decoding encoded words, base64, quoted-printable and the declared charset, and the F3 text view uses it. Missing: nothing reads the `Authentication-Results` header that a receiving server writes with the checks it made, and nothing summarises the SPF, DKIM and DMARC results it records. Fits: a pure parser in `iConsole/Services` that reuses the header reading of `MimeMessageReader`, shown in the F3 view of an `.eml`. It is not the DNS and email audit in security-tools 3.3, which looks records up; this reads what the receiver recorded, so the verdict must say that the result is the receiver's (security-tools section 5, rule 9). **Seen in:** RFC 8601 (Proposed Standard, May 2019, obsoletes RFC 7601). The header is `Authentication-Results`; its value starts with an authentication service identifier, then an optional version, then `none` or one or more `method=result` entries, each with an optional reason and `property=value` pairs. The registry defines the methods `auth`, `dkim`, `spf`, `iprev`, `vbr`, `dkim-atps`, `dkim-adsp`, `rrvs` and `smime`, and lists `domainkeys` and `sender-id` as deprecated ([RFC 8601](https://www.rfc-editor.org/rfc/rfc8601)). **Not found:** `authentication-results`, `dkim`, `dmarc`, `spf`, `rfc 8601`, `rfc 5322`. `mime` matches only the existing reader and the HAR viewer.

**Scanning and hardening**

14. **Container image vulnerability scan (Trivy, on demand).** Missing: the docker drive lists containers with `docker ps` (its format includes the image name, `iTerminal.Core/Services/Docker/DockerDriveService.cs`, line 50), and nothing scans an image or looks at a vulnerability database. Fits: an `IImageScanService` in `iTerminal.Core/Services/Docker`, run through `IExternalProcessRunner` on a background task, started from a container row on the `docker://` drive with the image name from its `Image` column, and a `TrivyRuntimeDependency`. The runtime-dependency pattern applies: a GitHub release asset checked against the digest GitHub records, as `OllamaRuntimeDependency` does, or the package-manager form, `SystemToolRuntimeDependency`; the maintainer chooses. A database update is a network call, so it is opt-in and the dialog states what is sent before the first run. This overlaps the package check in security-tools 3.6 (OSV); whether one tool serves both is the maintainer's choice. **Seen in:** the Trivy repository, licence Apache-2.0; its scan targets are container images, filesystems, remote Git repositories, virtual machine images and Kubernetes ([aquasecurity/trivy](https://github.com/aquasecurity/trivy)). The latest release is v0.76.2, marked Latest, published 5 November; the fetched page shows no year, and its footer reads 2026 ([releases](https://github.com/aquasecurity/trivy/releases/latest)). **Unverified:** whether the scan works offline with a downloaded database, and what a database update sends. Neither the README nor the trivy.dev home page says, and the documentation was not reached. **Not found:** `trivy`, `grype`, `syft`, `image scan`, `vulnerab`.

15. **Windows application control status (App Control for Business and AppLocker).** Missing: nothing reads whether an App Control policy or an AppLocker policy is applied on the machine. Fits: a read-only report, Windows only, beside the Windows permissions audit in security-tools 3.6, with "no policy found" stated as a fact and never as a verdict on the machine's security. It changes nothing. **Seen in:** the Microsoft Learn page "App Control and AppLocker Overview", last updated 29 March 2026. App Control for Business controls which drivers and applications may run on a Windows client, applies to the whole computer, and is designed as a security feature under the MSRC servicing criteria. The page says AppLocker does not meet those criteria, and that customers who can use App Control should use it rather than AppLocker. It links a feature-availability page by Windows version ([overview](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/appcontrol-and-applocker-overview)). **Unverified:** where the applied state is read from (no registry key or command was checked), and the Linux equivalents, SELinux and AppArmor, were not checked. **Not found:** `applocker`, `wdac`, `selinux`, `apparmor`, `smart app control`, `code integrity`.

**Secrets**

16. **Secret Service key for the password manager on Linux.** Partly present: `PasswordManager` seals its generated key and its verifier under the app's Data Protection keys, which on Windows are protected by the user's DPAPI. Its source comment says that elsewhere the keys are "only as private as the folder they are kept in" (`iTerminal.Core/Services/PasswordManager.cs`, `KeyProtector`). Missing: no call to the freedesktop Secret Service or to a macOS keychain, so on those systems the key sits in the app's folder. Fits: a platform store behind an `I*Service` in `iTerminal.Core`, asked for the key by `PasswordManager`, with a one-time move of an existing folder key into the store and no change to `passwords.sqlite`. Where the secret goes: the key is stored as one item in the user's keyring, labelled with the app's name and a purpose attribute; never in `Settings`, a URL, a log line or a command line (CLAUDE.md, credentials). A Secret Service prompt cannot be answered in a terminal, so the store must refuse and say how to unlock, not wait. **Seen in:** the freedesktop Secret Service API, draft version 0.2. Collections and items each hold a secret, lookup attributes and a label. `Unlock` returns the objects it could unlock at once, and may return a prompt for the rest. The specification requires neither a master password nor access control ([specification](https://specifications.freedesktop.org/secret-service/latest-single)). The fetched page is dated 20 November 2026, while a search summary gave 8 April 2026 for the latest draft, so the date is unresolved. A search snippet reports an open issue that the API cannot unlock with a known secret without a prompt ([xdg-specs issue 74](https://gitlab.freedesktop.org/xdg/xdg-specs/-/issues/74)); its current status is unverified. The macOS keychain was not verified: the fetched Apple page gave only its title. **Not found:** `secret-service`, `libsecret`, `secret service`, `keychain`, `CredentialManager`, `CredRead`, `ProtectedData`. `keyring` matches only gpg's keyring in the signature checks.

**Integrity**

17. **Known-good file set (NIST NSRL RDS).** Missing: nothing holds a set of known file hashes, so no file can be marked as a catalogued, unmodified file. Fits: a read-only lookup over the hashes that `ChecksumService` already computes, used by `HashManifestDialog` (verify) and `DuplicateFinderService` to label a row as in the set. It must never be labelled "safe": the NIST page says some entries are malicious tools, so a hit means the bytes match a catalogued file and nothing more. The set is large, so it would be an on-demand data pack following the Whisper pattern in `runtime-dependencies.md` (a pinned URL, with the size and SHA-256 checked by `VerifiedDownload`), once its terms allow it. **Seen in:** the NIST NSRL page (updated 31 July 2026). The Reference Data Set holds file fingerprints of known, traceable software, some of it malicious, and no hash values of illicit data ([NSRL](https://www.nist.gov/itl/csd/secure-systems-and-applications/national-software-reference-library-nsrl)). The page names no release identifier and no hash algorithm, and links a redistribution policy and a distribution schedule that were not read, so the licence and terms are unverified. A search summary says each release is digitally signed and that releases come quarterly (unverified). **Not found:** `nsrl`, `known-good`, `known good`.

**Already covered (no new item)**

- SPDX SBOM reader: survey 1 item 4, which records that SPDX was not checked there.
- Package vulnerability check through OSV: security-tools 3.6. Item 14 is a scanner over images and may be merged with it.
- Sigstore `.bundle` and cosign files: security-tools section 2 (Have), reported as not checked.
- DNS, SPF, DKIM and DMARC record checks: security-tools 3.3. Item 13 reads the receiver's recorded result instead.
- Windows permissions audit: security-tools 3.6. Item 15 is a different control.
- Bidi and hidden-character file names: present in `FileDisguise`. Item 12 extends it.

**Considered and not added in this run**

- Encrypted folder drive (a Cryptomator-style vault): not researched. It is a new drive scheme and a large change, for a later survey.
- Certificate Transparency log search: not researched. It sends a domain name to a third party, so it needs an opt-in design first.
- HTTP security-header grader (HSTS, CSP): not researched. It would extend the HTTP headers check in the network tools.
- Kernel sandboxes such as Landlock: not researched. They belong with the sandbox item in security-tools 3.2.
- ML-KEM: not researched. It belongs with item 3 of survey 1.
- No candidate was dropped as abandoned.

**Suggested order (survey 2 items)**

1. Git commit signature status (item 10): one format string and a model field, with no new dependency.
2. Authentication-Results reader (item 13): a pure parser beside the existing `.eml` reader.
3. Look-alike file names (item 12): extends an existing pure check.
4. Application control status (item 15): read-only and Windows only.
5. in-toto attestation check (item 9): a new parser and a digest check. The format is still under development.
6. Known-good file set (item 17): waits on the NSRL terms.
7. Container image scan (item 14): a runtime dependency and a network database. The maintainer picks the tool.
8. Secret Service key (item 16): changes where a key lives, so it needs a migration and a Linux test host.
9. PDF signature inspector (item 11): waits on a readable copy of the PAdES text.

**Unverified in survey 2**

- Item 9: the year of in-toto v1.2.0, the repository licence, and the SLSA predicate link (search snippet).
- Item 10: whether git runs the verifier once per signed commit, and the letters for SSH-signed commits.
- Item 11: every PAdES detail, because the fetched PDF could not be read.
- Item 12: the licence of the confusables data file.
- Item 14: the year of Trivy v0.76.2, offline use, and what a database update sends.
- Item 15: where the applied state is read from, and the Linux equivalents.
- Item 16: the date of the Secret Service draft, the status of issue 74, and the macOS keychain.
- Item 17: the NSRL licence and terms, the release signature, and the hash algorithms.

#### Survey 3: certificate revocation and transparency, header grading, Linux sandboxing and file integrity, OpenPGP packets and media sanitisation

Date 2026-11-20. Basis: official documentation (RFCs, a W3C draft, the Linux kernel documentation, a NIST publication page, Microsoft Learn and the OWASP cheat sheet), not run-time behaviour. Where a fact came only from a search snippet, or a page could not be read as text, the item says so. The status of items 1 to 17 was re-checked on branch `docs/research-security-survey-3` at commit `3ba7401f` (the same tree as `main`). One status changed, recorded under the status heading at the end of this survey; the rest still match the source. "Not found" means a case-insensitive grep of `iConsole/` and `iTerminal.Core/` (`*.cs`) on that branch and commit, with the terms listed in the item.

**Revocation and transparency**

18. **Certificate revocation status (CRL and OCSP).** Partly present: `AuthenticodeVerifier` checks revocation for the whole chain on Windows (`WTD_REVOKE_WHOLECHAIN`, `iTerminal.Core/Services/Updates/AuthenticodeVerifier.cs`), and `PdfSignatureReader` sets `X509RevocationMode.NoCheck` on purpose, because a viewer must not reach the network (`iConsole/Pdf/PdfSignatureReader.cs`, line 295). The certificate reader (`iConsole/Certificates/CertificateReader.cs`) names extended key usages, such as "OCSP Signing", and builds no chain. Missing: a status check for a certificate the user picks (the F3 certificate view, or the TLS inspector in security-tools 3.3), with the responder and distribution addresses read from the certificate, an opt-in request, and a verdict that names its basis: what the responder said, or that it did not answer. Fits: an address reader in `iConsole/Certificates` (a pure parse), and a service in `iTerminal.Core/Services` that runs on a background task. The dialog names the responder before a request is sent, as security-tools section 5 rule 8 requires, and the request carries the certificate's serial number and issuer hashes, never the file. A .NET chain built in `Online` mode can wait for the authority, so the check stays off the draw path. **Seen in:** RFC 6960, the Online Certificate Status Protocol, a Proposed Standard of June 2013, updated by RFC 8954 and RFC 9654 ([RFC 6960](https://www.rfc-editor.org/info/rfc6960)). `X509RevocationMode` has the values `NoCheck`, `Online` and `Offline`, and its remarks warn that an online check can take a long time while the authority is contacted ([X509RevocationMode](https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509revocationmode)). A search result names RFC 9919, a lightweight OCSP profile, dated July 2026; that is **unverified** because the page was not fetched. **Not found:** `cRLDistributionPoints`, `authorityInformationAccess`, `ocsp url`, `crl distribution`. The words `OCSP` and `revocation` match only the label "OCSP Signing", the Authenticode verifier, the PDF reader and a comment.

19. **Certificate Transparency inclusion.** Missing: a certificate's logging state is not shown. The certificate reader does not look for embedded signed certificate timestamps, and nothing talks to a transparency log. Fits: a pure parser in `iConsole/Certificates` that lists the timestamps embedded in a certificate and the log IDs they name, read offline; and, as an opt-in step, a query to one log the user chooses, by leaf hash. A log does not answer a question about a domain name: its endpoints take an entry index, a tree size or a leaf hash. A "find every certificate for this domain" feature would therefore fetch entries and filter them on the client, or send the domain to a third-party index, which the rules in security-tools section 5 forbid by default. A result says that a log reports the entry, never that the certificate is trustworthy. **Seen in:** RFC 9162, Certificate Transparency Version 2.0, Experimental, December 2021, obsoleting RFC 6962. It introduces the `transparency_info` TLS extension in place of `signed_certificate_timestamp`, and its log endpoints sit under `/ct/v2/` (`get-sth`, `get-proof-by-hash`, `get-entries` and others). The text names no lookup by domain, and says that monitors filter entries on the client ([RFC 9162](https://www.rfc-editor.org/rfc/rfc9162.html)). **Unverified:** the layout of the embedded timestamp list inside a certificate, which the read part of the RFC did not cover; the current list of logs. **Not found:** `certificate transparency`, `precertificate`, `signed certificate timestamp`, `transparency log`, `crt.sh`, `\bsct\b`.

**Headers**

20. **HTTP security-header grade.** Partly present: the HTTP headers tool (`NetworkToolService.HttpHeadersAsync`, `iTerminal.Core/Services/NetworkTools/NetworkToolService.cs`) sends a HEAD request and writes every response header it receives, with no judgement. Missing: a grader over those headers. For each recommended header (Strict-Transport-Security, Content-Security-Policy, X-Content-Type-Options, Referrer-Policy, the cross-origin policies and Permissions-Policy) it would say present, absent or weak; and it would flag Server and X-Powered-By as headers to remove, and Expect-CT and Public-Key-Pins as deprecated. Fits: a pure function in `iConsole` over the header lines the tool already holds, shown in the same dialog. A result is a hint about one response, never a verdict on the site. Its HSTS line states the cost as well: a long max-age that the certificate cannot back locks users out. **Seen in:** the OWASP HTTP Headers Cheat Sheet, which lists the recommended headers, the headers to remove, the deprecated ones and the HSTS guidance; the page carries no date ([HTTP Headers Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html)). RFC 6797, HTTP Strict Transport Security, Proposed Standard, November 2012 ([RFC 6797](https://www.rfc-editor.org/info/rfc6797)). Content Security Policy Level 3, a W3C Working Draft dated 27 October 2026, not yet a Recommendation ([CSP Level 3](https://www.w3.org/TR/CSP3/)). **Not found:** `strict-transport-security`, `content-security-policy`, `x-content-type-options`, `x-frame-options`, `hsts`.

**Linux sandboxing**

21. **Landlock restriction for a launched program (Linux only).** Missing: there is no Landlock, `no_new_privs` or `prctl` call, and no bubblewrap or firejail call. The only program starts are the file launcher, the command line and the run-as path (file-operations.md). This is the Linux part of the opt-in sandboxed run in security-tools 3.2. The restriction must be applied in the child before it runs the program: Landlock cannot be removed from the process that applies it, and children inherit it, so applied in iTerminal it would restrict the app itself. Fits: a small helper that the launcher starts through `IExternalProcessRunner`. The helper reads the ABI version and drops the rights the kernel does not know, as the kernel documentation advises. The dialog says what the restriction does not stop: the kernel page says `stat` and `chmod` cannot yet be restricted, and that pipes, sockets and namespace files under `/proc` are not restricted directly. Windows and macOS have no equivalent in this item. **Seen in:** Landlock, the kernel's userspace documentation, described for kernel 7.4.0-rc3, a development version. It lists ABI versions 1 to 12 (ABI 1 from Linux 5.13), restricts file access, TCP ports (from ABI 4), UDP ports (ABI 10), scopes (ABI 6) and capabilities and namespaces (ABI 12), stacks up to 16 layers and passes the restriction to children ([Landlock](https://docs.kernel.org/userspace-api/landlock.html)). **Unverified:** the licence of the kernel documentation, and which ABI version a given distribution kernel carries. **Not found:** `landlock`, `no_new_privs`, `prctl`, `bubblewrap`, `firejail`. `sandbox` matches only test seams and comments.

**OpenPGP**

22. **OpenPGP signature packet reader.** Partly present: `SignatureService` runs `gpg --status-fd 1 --batch --no-tty --verify` and reads the status lines, including `GOODSIG` (`iTerminal.Core/Services/Signing/SignatureService.cs`, around lines 181 and 200). gpg parses the packets, so when gpg is missing or has no key the dialog cannot name the signer. Missing: a reader of the packets of a detached `.asc` file that gives the signature version, the signature type, the public-key and hash algorithms, and the signer's key ID or fingerprint, with no key lookup. Fits: a pure parser in `iConsole` beside `iConsole/Certificates`, used by `SignatureDialog` for a line such as "made by key X, not checked". The reader does not decide trust: the keyring rule of publisher signatures still applies (file-operations.md). **Seen in:** RFC 9580, OpenPGP, a Proposed Standard of July 2024, which obsoletes RFC 4880, 5581 and 6637. Its section 5.2.3 sets the layout of version 4 and version 6 signature packets: the version, the signature type, the algorithm IDs, the hashed and unhashed subpackets (with 4-octet lengths in version 6), a salt in version 6 and the algorithm values. The packet tags are in a registry table in section 5 ([RFC 9580](https://www.rfc-editor.org/rfc/rfc9580.html)). **Unverified:** how a version 6 signature names its signer (fingerprint, key ID or both), because the text read stopped before that part; the packet tag numbers. **Not found:** `rfc 9580`, `rfc 4880`, `signature packet`, `packet tag`, `OpenPgpPacket`. `openpgp` matches only labels and comments.

**Sanitisation**

23. **Sanitisation category and record for a wipe.** Partly present: `SecureWipeService` has `WipePattern.Random`, `ThreePass` (zeros, ones, random, which the comment ties to the DoD 5220.22-M scheme) and `SevenPass` (`iTerminal.Core/Services/Wipe/SecureWipeService.cs`, lines 7 to 16). The wipe dialog states what an overwrite cannot reach (file-operations.md). Missing: a sanitisation category, in the terms NIST uses, stated for each run; and a record of which method was applied to which path, because the wipe leaves no report. Fits: a label in the wipe dialog's result (`SecureWipeDialog`), and an optional note in the evidence notebook through `IEvidenceNotebookService.AddTextAsync` (`iTerminal.Core/Services/Evidence/`), which already keeps notes and a SHA-256 per copied file (panels-and-drives.md). A note holds the paths and the method name, never the wiped contents. **Seen in:** NIST SP 800-88 Revision 2, Guidelines for Media Sanitization, published September 2025 and superseding Revision 1 of 17 December 2014 ([NIST SP 800-88r2](https://csrc.nist.gov/pubs/sp/800/88/r2/final)). The CSRC page defines no categories, and the PDF did not come through as text. The names Clear, Purge and Destroy, and the statement that degaussing is now a purge technique, come from search summaries and are **unverified**. **Not found:** `800-88`, `degauss`, `crypto erase`, `cryptographic erase`, `media sanitiz`. `purge` matches only the recycle bin's delete for good.

**Integrity (Linux)**

24. **fs-verity status of a file (Linux only).** Missing: nothing reads the fs-verity state of a file, and no `FS_IOC_` call exists. Fits: a read-only row in `PropertiesDialog` (Ctrl+F12), read by `FS_IOC_MEASURE_VERITY` through the libc import pattern that `LinkService` and `FreedesktopTrash` already use, on a background task like the other property reads. The row says "not enabled", or shows the digest and its algorithm. That is a fact about the disk, not a verdict on the file. Enabling verity writes a tree to the disk and can take a long time on a large file, so it would need a separate y/n in the footer, or stay out. **Seen in:** the kernel's fs-verity documentation, described for kernel 7.4.0-rc3. `FS_IOC_MEASURE_VERITY` returns the digest in constant time; `FS_IOC_READ_VERITY_METADATA` has been available since Linux 5.12; ext4 and f2fs are supported from 5.4 and btrfs from 5.15. Without signatures the feature detects accidental corruption only, and authenticity needs a separate check ([fs-verity](https://docs.kernel.org/filesystems/fsverity.html)). **Unverified:** the ioctl numbers and the structure layouts, which the page did not give; whether the ext4 requirement has changed in later kernels. **Not found:** `fs-verity`, `fsverity`, `merkle`, `FS_IOC_`, `\bverity\b`. The libc import pattern appears in five files, so the P/Invoke route is established.

**Already covered (no new item)**

- Lynis and other Linux hardening audits: security-tools 3.6 (Unix permissions audit and benchmark-style report). Not researched in this survey.
- gitleaks and other secret scanners: security-tools 3.4 (secret scanner). Not researched in this survey.
- Evidence bundle: security-tools 3.1. Partly present through the evidence notebook (`iTerminal.Core/Services/Evidence/`, `evidence://`), which keeps a SHA-256 per entry, a verify command and a read-only drive. security-tools.md is read-only, so its section 3.1 is out of date and the maintainer should update it.
- Binary hardening flags (RELRO, BIND_NOW, the PE DLL characteristics): security-tools section 2 (Executable analysis), `iTerminal.Core/Services/Executables/ElfAnalyzer.cs`.
- TLS endpoint inspector and certificate expiry watch: security-tools 3.3. Item 18 adds revocation to the same inspector.
- OSV package check and Trivy image scan: security-tools 3.6 and survey 2 item 14.
- Authenticode revocation: present for publisher updates (`AuthenticodeVerifier`); item 18 covers the other certificates.

**Considered and not added in this run**

- WebAuthn, passkeys and FIDO2 for the vault: not researched. It needs a platform authenticator or a HID stack the app does not have, and belongs to a later survey.
- Sigstore Rekor transparency log lookup: not researched. The `.bundle` format is in security-tools section 2, and survey 1 item 6 covers a different format.
- Expect-CT and Public-Key-Pins: deprecated, so they appear only as hints in item 20.

**Suggested order (survey 3 items)**

1. HTTP security-header grade (item 20): a pure function over headers the tool already reads.
2. OpenPGP signature packet reader (item 22): a pure parser that names the signer without gpg.
3. Certificate revocation status (item 18): an opt-in network call, built on the viewer's certificate reader.
4. fs-verity status (item 24): a read-only Linux row through a libc call the app already makes elsewhere.
5. Sanitisation category and record (item 23): reuses the wipe service and the evidence notebook, once the NIST categories are read from the PDF.
6. Certificate Transparency inclusion (item 19): an offline parse first; log queries wait for a decision on what they send.
7. Landlock restriction (item 21): the largest item, Linux only, and it waits for the opt-in sandbox decision in security-tools section 7.

**Unverified in survey 3**

- Item 18: RFC 9919 and its date (search snippet only), and the responder fields read from a certificate.
- Item 19: the layout of embedded timestamps, and the list of logs.
- Item 21: the licence of the kernel documentation, and the ABI of a distribution kernel.
- Item 22: how a version 6 signature names its signer, and the packet tag numbers.
- Item 23: the category names Clear, Purge and Destroy, and the degaussing statement (search summaries only; the NIST PDF did not come through as text).
- Item 24: the ioctl numbers and structure layouts, and the ext4 requirement in later kernels.

#### Status after the pull of 2026-11-20

- Item 11 (PDF signature inspector) has changed. Survey 2 found no change at commit `fcc5640e`, but commit `64c93224` (#692, "Show embedded PDF signatures and their verification in the F3 viewer") comes after it. It adds `iConsole/Pdf/PdfSignatureReader.cs`, which finds the `/ByteRange` and signature dictionaries, checks whether the signed bytes still match, and checks the signer's chain with revocation off (`NoCheck`). The F3 viewer shows a "Digital signatures" section (`iConsole/Dialogs/FileViewerDialog.cs`). The item is now partly present: listing and the integrity and chain checks exist; the PAdES profile, the revocation check and the signer's trust decision do not, as the file-operations write-up states.
