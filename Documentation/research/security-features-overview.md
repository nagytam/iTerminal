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
