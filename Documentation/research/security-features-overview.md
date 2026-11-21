# Security features overview for iTerminal

This document collects security features and tools that could suit iTerminal, a dual-pane file manager and terminal. It is a research record, not a plan. Each item has one current status: Missing (not built), Partly (some of it is built, the rest is listed), or Built. The prior record of what iTerminal already has, and of the features proposed before this document, is `Documentation/security-tools.md`. This document does not repeat it; it cites its sections as "security-tools N.N".

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

There are 32 items. Each appears once, under its group, with its latest status. Item numbers are the numbers given when the item was first surveyed and are not renumbered. Where an item's evidence or status changed after a later pull, the latest status is the one shown, and the earlier text is kept only where it still gives a fact.

### Integrity

#### 1. BLAKE3 digest

- **Status:** Missing. `ChecksumAlgorithm` has MD5, SHA-1, SHA-256 and SHA-512 only (`iTerminal.Core/Services/Checksums/ChecksumService.cs`). `ChecksumAlgorithm` and `IncrementalHash` are present, as the fit line shows.
- **Fits:** `ChecksumService` (the enum, `Algorithms`, `NameOf`), and the algorithm lists in `ChecksumDialog` and `HashManifestDialog`. The .NET 11 namespace has no BLAKE3 type, so a package or a port is needed, which is the maintainer's choice. A BLAKE3 manifest would not verify with `sha256sum`, so the manifest format needs its own algorithm name.
- **Sources:** BLAKE3 README, which names a public-domain (CC0 1.0) or Apache-2.0 licence and points to the design paper and the C2SP specification ([BLAKE3](https://github.com/BLAKE3-team/BLAKE3)). Latest release 1.8.7, dated 20 August ([releases](https://github.com/BLAKE3-team/BLAKE3/releases)).
- **Size:** not estimated in the survey. Effort note: a small change to the checksum enum, once a maintained implementation is chosen.
- **Not found:** `blake`, `blake3`.
- **Unverified:** the year of BLAKE3 release 1.8.7 (the fetched page shows no year).

#### 17. Known-good file set (NIST NSRL RDS)

- **Status:** Missing. Nothing holds a set of known file hashes, so no file can be marked as a catalogued, unmodified file.
- **Fits:** a read-only lookup over the hashes that `ChecksumService` already computes, used by `HashManifestDialog` (verify) and `DuplicateFinderService` to label a row as in the set. It must never be labelled "safe": the NIST page says some entries are malicious tools, so a hit means the bytes match a catalogued file and nothing more. The set is large, so it would be an on-demand data pack following the Whisper pattern in `runtime-dependencies.md` (a pinned URL, with the size and SHA-256 checked by `VerifiedDownload`), once its terms allow it.
- **Sources:** NIST NSRL page, updated 31 July 2026. The Reference Data Set holds file fingerprints of known, traceable software, some of it malicious, and no hash values of illicit data ([NSRL](https://www.nist.gov/itl/csd/secure-systems-and-applications/national-software-reference-library-nsrl)).
- **Size:** not estimated in the survey. Effort note: waits on the NSRL terms.
- **Not found:** `nsrl`, `known-good`, `known good`.
- **Unverified:** the NSRL licence and terms (the page links a redistribution policy and a distribution schedule that were not read), the release signature, and the hash algorithms. A search summary says each release is digitally signed and that releases come quarterly (unverified).

#### 24. fs-verity status of a file (Linux only)

- **Status:** Missing. Nothing reads the fs-verity state of a file, and no `FS_IOC_` call exists.
- **Fits:** a read-only row in `PropertiesDialog` (Ctrl+F12), read by `FS_IOC_MEASURE_VERITY` through the libc import pattern that `LinkService` and `FreedesktopTrash` already use, on a background task like the other property reads. The row says "not enabled", or shows the digest and its algorithm. That is a fact about the disk, not a verdict on the file. Enabling verity writes a tree to the disk and can take a long time on a large file, so it would need a separate y/n in the footer, or stay out.
- **Sources:** the kernel's fs-verity documentation, described for kernel 7.4.0-rc3. `FS_IOC_MEASURE_VERITY` returns the digest in constant time; `FS_IOC_READ_VERITY_METADATA` has been available since Linux 5.12; ext4 and f2fs are supported from 5.4 and btrfs from 5.15. Without signatures the feature detects accidental corruption only, and authenticity needs a separate check ([fs-verity](https://docs.kernel.org/filesystems/fsverity.html)).
- **Size:** not estimated in the survey. Effort note: a read-only Linux row through a libc call the app already makes elsewhere.
- **Not found:** `fs-verity`, `fsverity`, `merkle`, `FS_IOC_`, `\bverity\b`. The libc import pattern appears in five files, so the P/Invoke route is established.
- **Unverified:** the ioctl numbers and the structure layouts, which the page did not give; whether the ext4 requirement has changed in later kernels.

#### 27. TUF signed metadata for self-update

- **Status:** Partly. A self-update downloads the release's `.msi` or portable archive, checks it against the `sha256` digest GitHub records, and checks its Authenticode signature (`iTerminal.Core/Services/Updates/UpdateService.cs` and `AuthenticodeVerifier.cs`; runtime-dependencies.md, Self-update).
- **Missing:** signed metadata for the release, signed by a key separate from the GitHub account, with expiry times, version numbers and a rollback check, so that one compromised account cannot change a file and its digest together.
- **Fits:** an `IUpdateMetadataVerifier` in `iTerminal.Core/Services/Updates`, run by the update check before a download, with the trusted root key carried in the installer and never in `Settings` or a URL. Systems other than Windows get only a notice today (runtime-dependencies.md), so the first effect is on Windows.
- **Sources:** the TUF specification, version 1.0.37, last modified 8 October 2026. It names four top-level roles (root, targets, snapshot and timestamp) and an optional mirrors role, and states no licence on the fetched page ([TUF specification](https://theupdateframework.github.io/specification/latest/)).
- **Size:** not estimated in the survey. Effort note: the largest trust decision in survey 4, needing a key bootstrap and rollback rules.
- **Not found:** `\btuf\b`, `update framework`, `root.json`, `targets.json`, `snapshot.json`, `timestamp.json`.
- **Unverified:** the role description of the timestamp key as an online key re-signed on a schedule (search summary only).

#### 28. Fuzzy hash for near-duplicate files

- **Status:** Missing. Duplicates are found by size and then by exact SHA-256 only (`iTerminal.Core/Services/Duplicates/DuplicateFinderService.cs`), so a file that differs by a few bytes from another is never reported as related.
- **Fits:** a pure digest and difference function in `iConsole` (a folder beside `iConsole/Security`), and a "similar" mode in `DuplicateFinderService` that shows the score with the threshold it used. It must not feed `ChecksumService`, whose results are exact. The score is a hint about two files and never proof that they share an origin or that either is harmful. The class `FuzzyMatcher` in `iConsole/Helper` is a text-completion matcher and is not this.
- **Sources:** the TLSH repository ([trendmicro/tlsh](https://github.com/trendmicro/tlsh)). Its LICENSE file grants a choice of Apache License 2.0 or BSD 3-Clause ([LICENSE](https://raw.githubusercontent.com/trendmicro/tlsh/master/LICENSE)). The README says a digest needs at least 50 bytes, begins with `T1` and is compared with a difference score in which 0 means almost identical.
- **Size:** not estimated in the survey. Effort note: a pure function, after a licence choice between Apache-2.0 and BSD-3-Clause and a test corpus.
- **Not found:** `tlsh`, `ssdeep`, `sdhash`, `fuzzy hash`, `locality sensitive`.
- **Unverified:** the TLSH release date (the page lists tag 5.0.2 with a date that reads "10/07/2026", which can be read two ways and has no release section); a NuGet package `Tlsh.net` named by a search result, whose licence and upkeep are unverified; ssdeep was not checked, because the search returned nothing on it.

### Encryption

#### 2. Argon2id key derivation for `.enc` files

- **Status:** Missing. The file format has one key derivation id (`KdfPbkdf2Sha256 = 1`), and the reader refuses any other id as unsupported (`FileEncryptionService`, header byte 6, and the format table in file-operations.md).
- **Fits:** `FileEncryptionService`. A header that names a memory cost would need the same bounds as the iteration count (`MinIterations`, `MaxIterations`), or a hostile file could exhaust memory.
- **Sources:** RFC 9106 (Informational, September 2021), which recommends Argon2id when the threat model is unknown, gives two parameter sets with four lanes (one pass with 2 GiB, or three passes with 64 MiB), and recommends a 128-bit salt ([RFC 9106](https://www.rfc-editor.org/rfc/rfc9106)). The .NET 11 namespace lists `Rfc2898DeriveBytes` and no Argon2 type, so a package is needed ([System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography?view=net-11.0)). Konscious.Security.Cryptography.Argon2 1.3.1 is MIT-licensed and implements Argon2d, Argon2i and Argon2id; its last release is dated 19 June 2024, more than a year before this survey, so its upkeep must be checked before adoption ([NuGet](https://www.nuget.org/packages/Konscious.Security.Cryptography.Argon2)). A NuGet package is shipped, not downloaded, so it does not follow the runtime-dependency pattern.
- **Size:** not estimated in the survey. Effort note: needs a package and bounds in the header.
- **Not found:** `argon`, `scrypt`. `kdf` and `pbkdf2` match `FileEncryptionService`, which uses PBKDF2 only.
- **Related:** Argon2 verification of stored hashes is security-tools 3.5. This item is a different use, a key for files, and is not a duplicate.

#### 29. Encrypted folder drive (Cryptomator-style vault)

- **Status:** Missing. Encryption works per file (`FileEncryptionService`, one `.enc` beside each file), so an encrypted folder cannot be browsed as a folder and no folder vault exists.
- **Fits:** a new URL scheme, following the checklist in panels-and-drives.md (the `Container` constant, a `Url` branch, a `ContentModelService.AddContentModel` case, a DI registration, `FolderUrlValidationModel.SupportedSchemes` and the Places page). The vault passphrase comes from a password-manager entry, never from a URL, and the vault's key material and configuration sit in files inside the vault folder, not in `Settings`.
- **Sources:** the Cryptomator security documentation, which describes file contents encrypted with AES-GCM in 32 KiB chunks, file names encrypted with AES-SIV using the parent directory's ID as associated data, and a configuration file named `vault.cryptomator`. The page names no format version and states no licence ([Cryptomator vault](https://docs.cryptomator.org/security/vault/)).
- **Size:** not estimated in the survey. Effort note: a new drive scheme and format, so a large change.
- **Not found:** `cryptomator`, `masterkey`, `vault.cryptomator`, `\bSIV\b`, `vault://`, `gocryptfs`, `encfs`.
- **Unverified:** Cryptomator format 8 and its introduction in Cryptomator 1.6.0 (19 October 2021), both from a search summary; the Cryptomator licence was not checked.

### Signatures and provenance

#### 3. ML-DSA signatures

- **Status:** Missing. The in-process `.sig` format (`iTerminal-signature-v1 <rsa|ecdsa> <base64>`) knows two algorithm names; any other name is reported as "could not check" by `SignatureService` (`iTerminal.Core/Services/Signing/SignatureService.cs`).
- **Fits:** `SignatureService` (`PemPrefix`, `VerifyRsa`, `VerifyEcdsa`, the algorithm choice in `SignPemAsync`), and the verdict lines of `SignatureDialog`. A verifier must still refuse a signature made with another algorithm, as the existing PEM method already does.
- **Sources:** the MLDsa class reference, which states that the algorithm is specified by FIPS 204, that `IsSupported` depends on the platform, and that the class imports and exports PEM keys, signs and verifies data ([MLDsa](https://learn.microsoft.com/dotnet/api/system.security.cryptography.mldsa)). The fetched page shows no experimental note.
- **Size:** not estimated in the survey. Effort note: one more algorithm in the in-process format, with the platform API already in .NET 11.
- **Not found:** `mldsa`, `ml-dsa`, `fips 204`, `post-quantum`, `pqc`, `mlkem`, `slhdsa`.
- **Unverified:** a search snippet says .NET 10 marks the class experimental under SYSLIB5006; this is unverified against the fetched page.

#### 6. C2PA content credentials viewer

- **Status:** Missing. There is no manifest reader. The word "manifest" in the code refers to hash manifests only.
- **Fits:** a read-only viewer for the image types the reader supports, placed in `iConsole` if a managed reader exists, or in `iTerminal.Core/Services` for a native one. The reference implementation is Rust. A .NET build would call its C interface, which is a native library: shipped, or installed on demand under the runtime-dependency pattern if downloaded. That choice is the maintainer's. A verdict must say that a signature is valid for its issuer; whether to trust the issuer is the user's decision.
- **Sources:** the c2pa-rs README, dual-licensed MIT or Apache-2.0, which names version 2.4 of the C2PA specification and says the library reads and validates manifests as well as signing them ([c2pa-rs](https://github.com/contentauth/c2pa-rs)). The most recent release on the page is the C interface, c2pa-c-ffi v0.92.2, published 16 November (2026, per the release notes on the page) ([releases](https://github.com/contentauth/c2pa-rs/releases)).
- **Size:** not estimated in the survey. Effort note: needs a native-library decision.
- **Not found:** `c2pa`, `jumbf`, `content credential`, `provenance`.
- **Unverified:** a Rust crate version of 0.90.17 (search snippet); the image types the reader supports (the README does not list them, so check before building).

#### 9. in-toto attestation check

- **Status:** Missing. Nothing reads an attestation (a `Statement` with its `subject` digests and `predicateType`), and no subject digest is compared with a file.
- **Fits:** a pure JSON reader in `iConsole` for the statement, a subject check in `iTerminal.Core/Services/Checksums` that uses `ChecksumService` for the file's SHA-256, and an F3 viewer that recognises a statement by its `_type` field rather than by file name. A matching digest says the file is the one the statement names, and nothing about whether the builder is trusted; the verdict has to say so (security-tools section 5, rule 9). Checking the signature on the envelope is a separate step that needs a decision on the verifier.
- **Sources:** the in-toto Attestation Framework repository, which describes a format specification with maintainer-vetted predicates, says the project is still under development, and names language bindings for Go, Python, Rust and Java ([in-toto/attestation](https://github.com/in-toto/attestation)). Its latest release is v1.2.0, marked Latest ([releases](https://github.com/in-toto/attestation/releases)); the release notes for v1.1.0 give the statement type `https://in-toto.io/Statement/v1`. A search snippet says SLSA provenance is one of the in-toto predicate types ([SLSA blog](https://slsa.dev/blog/2023/05/in-toto-and-slsa)).
- **Size:** not estimated in the survey. Effort note: a new parser and a digest check; the format is still under development.
- **Not found:** `in-toto`, `intoto`, `slsa`, `attestation`, `provenance`. `cosign` and `sigstore` match only the publisher-signature code, which reports sigstore files as not checked.
- **Unverified:** the year of in-toto v1.2.0 (no year on the fetched page); the repository licence (the page names none); the SLSA predicate link (search snippet).

#### 10. Git commit and tag signature status

- **Status:** Missing. The history of the `git://` drive reads `%H`, `%h`, `%ct` and `%s` (`iTerminal.Core/Services/Git/GitDriveService.cs`, the `log` format near line 142; the HEAD line near line 94 reads `%H`, `%ct` and `%s`). The `GitCommit` record of the git client's log (`iTerminal.Core/Model/Git/GitCommit.cs`) holds a short hash, subject, author and relative date with no signature field. A signed commit therefore looks like any other.
- **Fits:** the format string in `GitDriveService` (adding `%G?` and `%GS`), a field on `iTerminal.Core/Model/Git/GitCommit.cs`, and the history listing of the `git://` drive. A letter `G` says the signature checks against a key in the user's keyring; it does not say the key is trusted, which is the rule the publisher-signature check already follows.
- **Sources:** the git pretty-formats manual, which documents Git 2.54.0 and says the text is unchanged through 2.56.1 ([pretty-formats](https://git-scm.com/docs/pretty-formats)). `%G?` prints one letter: G good, B bad, U good with unknown validity, X good but expired, Y good made by an expired key, R good made by a revoked key, E cannot be checked, N no signature. `%GS` prints the signer's name, `%GK` the signing key, `%GF` its fingerprint, `%GT` the trust level and `%GG` the raw output of the verifier.
- **Size:** not estimated in the survey. Effort note: one format string and a model field, with no new dependency.
- **Not found:** `show-signature`, `%G?`, `verify-commit`, `verify-tag`, `gitsign`, `gpgsig`, `commit signature`, `signature status`.
- **Unverified:** whether git runs the verifier once for each signed commit (a cost on a 100-commit listing), and whether SSH-signed commits give the same letters.

#### 11. PDF signature inspector

- **Status:** Partly. Commit `64c93224` (#692, "Show embedded PDF signatures and their verification in the F3 viewer") adds `iConsole/Pdf/PdfSignatureReader.cs`, which finds the `/ByteRange` and signature dictionaries, checks whether the signed bytes still match, and checks the signer's chain with revocation off (`NoCheck`). The F3 viewer shows a "Digital signatures" section (`iConsole/Dialogs/FileViewerDialog.cs`).
- **Still missing:** the PAdES profile, the revocation check and the signer's trust decision, as the file-operations write-up states.
- **Earlier state:** at commit `fcc5640e` nothing in `iConsole/` or `iTerminal.Core/` read a PDF signature dictionary, its byte range or its signer. The first step only lists what is there. A listed signature is a fact about the file, never a verdict that it is valid.
- **Sources:** ETSI EN 319 142-1, the PAdES part 1 (baseline), which the search results give as version 1.2.1 dated 2024 ([ETSI EN 319 142-1](https://www.etsi.org/deliver/etsi_en/319100_319199/31914201/01.02.01_60/en_31914201v010201p.pdf)). The Adobe copy of the PDF 1.7 reference was over the fetch size limit and was not read (no link recorded).
- **Size:** not estimated in the survey. Effort note: waits on a readable copy of the PAdES text.
- **Not found (at commit `fcc5640e`):** `/ByteRange`, `/Sig`, `pades`, `xades`, `PdfSignature`, `digital signature`. The one `pades` hit is inside the word "Spades" in a game. `/ByteRange` now matches `PdfSignatureReader`.
- **Unverified:** every PAdES detail, because the fetched PDF could not be read as text. A search snippet says the signature dictionary holds `Filter`, `SubFilter`, `ByteRange` and `Contents`, and that the byte range covers the file except the signature value (unverified).

#### 22. OpenPGP signature packet reader

- **Status:** Built. `iConsole/OpenPgp/OpenPgpSignatureReader.cs` reads the first signature packet of a detached signature, armored or binary, for versions 3, 4 and 6: version, signature type, public-key and hash algorithms, creation time, and the issuer fingerprint or key ID. `SignatureService` reads the file once before gpg runs and puts the packet on the gpg verdict (`SignatureVerdict.Packet`); `SignatureDialog` shows it as two facts lines under the gpg verdict. Parser tests use a gpg 2.4.9 v4 signature and hand-built v3 and v6 packets.
- **Left out on purpose:** key lookup, cryptographic verification, trust decisions, and cleartext-signed messages. gpg's verdict is still the only check; the packet line is a fact about the file, and the keyring rule of publisher signatures still applies (file-operations.md). A version 6 signature carrying an Issuer Key ID subpacket has it ignored and flagged.
- **Sources:** RFC 9580, OpenPGP, a Proposed Standard of July 2024, which obsoletes RFC 4880, 5581 and 6637. Its section 5.2.3 sets the layout of version 4 and version 6 signature packets: the version, the signature type, the algorithm IDs, the hashed and unhashed subpackets (with 4-octet lengths in version 6), a salt in version 6 and the algorithm values. The packet tags are in a registry table in section 5 ([RFC 9580](https://www.rfc-editor.org/rfc/rfc9580.html)).
- **Size:** not estimated in the survey. Effort note: a pure parser that names the signer without gpg.
- **Not found:** `rfc 9580`, `rfc 4880`, `signature packet`, `packet tag`, `OpenPgpPacket`. `openpgp` matches only labels and comments.
- **Resolved on the 70fe8ad5 pass:** a version 6 signer is named by the issuer fingerprint (subpacket 33, which RFC 9580 section 5.2.3.35 says should be in every signature). The Issuer Key ID subpacket (16) must not appear in a version 6 signature (section 5.2.3.12), taken from the RFC's text as summarised, not read verbatim. The signature packet is tag 2, confirmed by the parser's tests against gpg output.

### Inventory

#### 4. CycloneDX SBOM reader

- **Status:** Missing as a reader. Nothing reads `bom.json`, `*.cdx.json` or `bom.xml`. Commit `4ad0edba` (#717) adds a writer, not a reader: `iTerminal.Core/Services/Dotnet/DotnetSolutionService.cs` (`GenerateSbom`, which writes `bomFormat` "CycloneDX" as XML or JSON), `iTerminal.Core/Model/Dotnet/SbomResult.cs`, and the S key of the solution viewer. The writer's output gives a reader test data.
- **Fits:** a parser in `iConsole` for the JSON and XML forms, an F3 viewer for those file names, and the component list as an input to the package check in security-tools 3.6.
- **Sources:** the CycloneDX overview, which names JSON, XML and Protocol Buffers as its formats and gives version 1.7 ([overview](https://cyclonedx.org/specification/overview/)); version 1.7 was released on 21 October 2025 ([announcement](https://cyclonedx.org/news/cyclonedx-v1.7-released)); the latest tag on the specification repository is 1.7.2, dated 17 September ([releases](https://github.com/CycloneDX/specification/releases/latest)).
- **Size:** not estimated in the survey. Effort note: a pure parser, and its component list feeds the package check in security-tools 3.6.
- **Not found (at commits before `4ad0edba`):** `cyclonedx`, `sbom`, `spdx`, `purl`, `.cdx`. At `4ad0edba` the term `cyclonedx` matches the writer.
- **Unverified:** the year of CycloneDX tag 1.7.2 (no year on the fetched page), and the licence of the specification (none named on the fetched pages). SPDX was not checked.

#### 26. OpenVEX statement reader

- **Status:** Missing. Nothing reads a VEX document, so a package or image report cannot say whether a listed vulnerability affects the product.
- **Fits:** a JSON reader in `iConsole` beside the CycloneDX reader (item 4), showing for each statement its product, vulnerability and status, with the author named. A statement is the publisher's claim, not a finding by iTerminal, and the verdict must say so (security-tools section 5, rule 9).
- **Sources:** the OpenVEX specification repository, which says the specification is a draft, is licensed CC0-1.0, uses the JSON-LD context `https://openvex.dev/ns/v0.2.0` and shows the status `fixed` in its example ([openvex/spec](https://github.com/openvex/spec)). The page shows no release tag.
- **Size:** not estimated in the survey. Effort note: a pure parser beside the CycloneDX reader, with the same test corpus idea.
- **Not found:** `openvex`, `\bvex\b`, `exploitability`, `csaf`.
- **Unverified:** the other status values (for example `not_affected`), because the status list was not reached; a search summary says the Go library go-vex v0.2.5 implements v0.2.0, and that Trivy and Grype accept OpenVEX files (both unverified).

### Triage

#### 5. Windows shortcut (.lnk) inspector

- **Status:** Missing. There is no shell-link reader. `.lnk` appears only in the Windows run-as list (`ElevationService.WindowsRunAsExtensions`, `iTerminal.Core/Services/Elevation/ElevationService.cs`). Enter on a `.lnk` file goes to the system's default program (file-operations.md), so the target of a shortcut is seen only by running it. Commit `265f9aee` adds `Test/FileTypes/example.lnk`, a sample file and not a parser.
- **Fits:** a binary parser in `iConsole` (header, link target ID list, link info and string data), an F3 viewer, and an info-strip line. The parser reads the target, arguments and working folder as text and never resolves, follows or launches them.
- **Sources:** MS-SHLLINK, the Shell Link Binary File Format, last updated 21 November 2025 ([introduction](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/99e8d0e5-5bc6-4aed-af37-da7f584f832a)). Its structures are a header, an optional target ID list, optional link info, optional string data and extra data ([structures](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/747629b3-b5be-452a-8101-b9a2ec49978c)). The string data holds a name, a relative path, a working folder, command-line arguments and an icon location ([StringData](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/17b69472-0f34-4bcf-b290-eccdb8de224b)).
- **Size:** not estimated in the survey. Effort note: a pure parser, reachable from F3, and it shows a target without running it.
- **Not found (at commit `41d889b2`):** `shelllink`, `IShellLink`. The term `.lnk` matches only the run-as list.

### Auditing

#### 7. RFC 3161 timestamp check for evidence

- **Status:** Partly. Commit `f283477a` (#703) stamps each evidence entry. `iTerminal.Core/Services/Timestamps/Rfc3161.cs` builds and reads the request and the response. `iTerminal.Core/Services/Timestamps/ITimestampAuthority.cs` sends only the SHA-256 digest to the authority named by the `EvidenceNotebook.TimestampAuthority` setting. `iTerminal.Core/Services/Evidence/EvidenceNotebookService.cs` checks each stamp with `Rfc3161.IsValidFor` (about line 237). The stamp is on by default; see the open issue for the maintainer below.
- **Still missing:** a check of a token the user supplies from outside the notebook, since no other use of `Rfc3161` or `TimeStampResp` was found.
- **Earlier state:** at commits `41d889b2` to `3ba7401f` there was no time-stamp request and no token check. "Timestamp" meant a file, quote or log time in the code.
- **Fits (for the missing token check):** the evidence bundle in security-tools 3.1, as an optional step that checks a token against the bundle's manifest. Parsing a token needs the ASN.1 reader in security-tools 3.5. A request sends data to a time-stamp authority, so it is opt-in and the dialog says what is sent (security-tools section 5, rule 8).
- **Sources:** RFC 3161, a Proposed Standard from August 2001 updated by RFC 5816, which defines how a client asks a time-stamp authority for a token and how the authority answers ([RFC 3161](https://www.rfc-editor.org/rfc/rfc3161)).
- **Size:** not estimated in the survey. Effort note (survey 1): needs an opt-in network call and the ASN.1 reader.
- **Not found (at the earlier commits):** `rfc 3161`, `time-stamp authority`, `tsa`.
- **Unverified:** a search snippet says the authority receives a hash and not the file; this is not verified against the RFC text.

#### 18. Certificate revocation status (CRL and OCSP)

- **Status:** Partly. `AuthenticodeVerifier` checks revocation for the whole chain on Windows (`WTD_REVOKE_WHOLECHAIN`, `iTerminal.Core/Services/Updates/AuthenticodeVerifier.cs`). `PdfSignatureReader` sets `X509RevocationMode.NoCheck` on purpose, because a viewer must not reach the network (`iConsole/Pdf/PdfSignatureReader.cs`, line 295). The certificate reader (`iConsole/Certificates/CertificateReader.cs`) names extended key usages, such as "OCSP Signing", and builds no chain.
- **Missing:** a status check for a certificate the user picks (the F3 certificate view, or the TLS inspector in security-tools 3.3), with the responder and distribution addresses read from the certificate, an opt-in request, and a verdict that names its basis: what the responder said, or that it did not answer.
- **Fits:** an address reader in `iConsole/Certificates` (a pure parse), and a service in `iTerminal.Core/Services` that runs on a background task. The dialog names the responder before a request is sent, as security-tools section 5 rule 8 requires, and the request carries the certificate's serial number and issuer hashes, never the file. A .NET chain built in `Online` mode can wait for the authority, so the check stays off the draw path.
- **Sources:** RFC 6960, the Online Certificate Status Protocol, a Proposed Standard of June 2013, updated by RFC 8954 and RFC 9654 ([RFC 6960](https://www.rfc-editor.org/info/rfc6960)). `X509RevocationMode` has the values `NoCheck`, `Online` and `Offline`, and its remarks warn that an online check can take a long time while the authority is contacted ([X509RevocationMode](https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509revocationmode)).
- **Size:** not estimated in the survey. Effort note: an opt-in network call, built on the viewer's certificate reader.
- **Not found:** `cRLDistributionPoints`, `authorityInformationAccess`, `ocsp url`, `crl distribution`. The words `OCSP` and `revocation` match only the label "OCSP Signing", the Authenticode verifier, the PDF reader and a comment.
- **Unverified:** RFC 9919, a lightweight OCSP profile, and its date of July 2026 (search snippet only, not fetched); the responder fields read from a certificate.
- **Related:** Authenticode revocation for publisher updates is present (`AuthenticodeVerifier`); this item covers the other certificates.

### Detection

#### 8. Sigma rules over event logs

- **Status:** Missing. There is no event-log reader and no rule matcher.
- **Fits:** a rule evaluator in `iConsole` (field matching, modifiers and the `condition` expression), fed by the Windows event log viewer and the authentication log summary in security-tools 3.7. It runs as a background task, and a rule hit is shown as a hint with its rule name, never as a verdict (security-tools section 5, rule 9).
- **Sources:** the sigma-specification repository, which holds the rules specification (`specification/sigma-rules-specification.md`), a JSON schema for rules, and appendices for modifiers, tags and the taxonomy of field names and log sources ([sigma-specification](https://github.com/SigmaHQ/sigma-specification)). The latest release is v2.1.0, dated 12 September ([releases](https://github.com/SigmaHQ/sigma-specification/releases)).
- **Size:** not estimated in the survey. Effort note: depends on the event-log reader in security-tools 3.7.
- **Not found:** `sigma`, `EventLog`, `EventRecord`, `winevt`, `journalctl`, `auth.log`. Four test files match the broader pattern through paths such as `/var/log`; none is a reader of an event log.
- **Unverified:** the year of Sigma specification v2.1.0 (no year on the fetched page); the licences of the specification and the rules (the fetched pages name none; a search snippet says the specification is public domain and the rules are under DRL 1.1).

### Names and mail

#### 12. Look-alike file names

- **Status:** Partly. `iConsole/Security/FileDisguise.cs` already finds bidi overrides and other bidi controls, zero-width and other control characters in a name (`DisguiseFindings.HiddenCharacters`), a double extension and a type mismatch. `FileDisguise.Mask` shows the names on the panel, in Properties and in the info strip.
- **Missing:** no check for characters that look alike across scripts (a Latin letter beside a Cyrillic one, for example), and no check for a name that mixes scripts.
- **Fits:** a pure function beside `FileDisguise.AnalyzeName` in `iConsole/Security`, a new `DisguiseFindings` flag, and a line in `FileDisguiseMessage`. Real names are often mixed-script, so the result is a hint with its basis, never a verdict.
- **Sources:** UTS #39 (Unicode Security Mechanisms), revision 34 (Version 18.0.0), dated 27 August 2026. Section 4 covers whole-script confusables (4.1) and mixed-script confusables (4.2), section 5.1 covers mixed-script detection, and the confusable mappings are published as a data file, `confusables.txt` ([UTS #39](https://www.unicode.org/reports/tr39/)).
- **Size:** not estimated in the survey. Effort note: extends an existing pure check.
- **Not found:** `confusable`, `homoglyph`, `homograph`, `mixed script`, `UnicodeScript`, `IdnMapping`, `Punycode`, `skeleton` (the only hit is the loading-placeholder control `Skeleton`). `NormalizationForm` is used only by the encryption passphrase.
- **Unverified:** the licence of the confusables data file (not checked).
- **Note:** item 12 was not re-checked in the latest pull.

#### 13. Authentication-Results reader for `.eml` messages

- **Status:** Partly. `iConsole/Services/MimeMessageReader.cs` reads an e-mail's addresses, date, subject, body and attachment names, decoding encoded words, base64, quoted-printable and the declared charset, and the F3 text view uses it.
- **Missing:** nothing reads the `Authentication-Results` header that a receiving server writes with the checks it made, and nothing summarises the SPF, DKIM and DMARC results it records.
- **Fits:** a pure parser in `iConsole/Services` that reuses the header reading of `MimeMessageReader`, shown in the F3 view of an `.eml`. It is not the DNS and e-mail audit in security-tools 3.3, which looks records up; this reads what the receiver recorded, so the verdict must say that the result is the receiver's (security-tools section 5, rule 9).
- **Sources:** RFC 8601 (Proposed Standard, May 2019, obsoletes RFC 7601). The header is `Authentication-Results`; its value starts with an authentication service identifier, then an optional version, then `none` or one or more `method=result` entries, each with an optional reason and `property=value` pairs. The registry defines the methods `auth`, `dkim`, `spf`, `iprev`, `vbr`, `dkim-atps`, `dkim-adsp`, `rrvs` and `smime`, and lists `domainkeys` and `sender-id` as deprecated ([RFC 8601](https://www.rfc-editor.org/rfc/rfc8601)).
- **Size:** not estimated in the survey. Effort note: a pure parser beside the existing `.eml` reader.
- **Not found:** `authentication-results`, `dkim`, `dmarc`, `spf`, `rfc 8601`, `rfc 5322`. `mime` matches only the existing reader and the HAR viewer.

### Scanning

#### 14. Container image vulnerability scan (Trivy, on demand)

- **Status:** Missing. The docker drive lists containers with `docker ps` (its format includes the image name, `iTerminal.Core/Services/Docker/DockerDriveService.cs`, line 50), and nothing scans an image or looks at a vulnerability database.
- **Fits:** an `IImageScanService` in `iTerminal.Core/Services/Docker`, run through `IExternalProcessRunner` on a background task, started from a container row on the `docker://` drive with the image name from its `Image` column, and a `TrivyRuntimeDependency`. The runtime-dependency pattern applies: a GitHub release asset checked against the digest GitHub records, as `OllamaRuntimeDependency` does, or the package-manager form, `SystemToolRuntimeDependency`; the maintainer chooses. A database update is a network call, so it is opt-in, and the dialog states what is sent before the first run. This overlaps the package check in security-tools 3.6 (OSV); whether one tool serves both is the maintainer's choice.
- **Sources:** the Trivy repository, licence Apache-2.0; its scan targets are container images, filesystems, remote Git repositories, virtual machine images and Kubernetes ([aquasecurity/trivy](https://github.com/aquasecurity/trivy)). The latest release is v0.76.2, marked Latest, published 5 November ([releases](https://github.com/aquasecurity/trivy/releases/latest)).
- **Size:** not estimated in the survey. Effort note: a runtime dependency and a network database; the maintainer picks the tool.
- **Not found:** `trivy`, `grype`, `syft`, `image scan`, `vulnerab`.
- **Unverified:** the year of Trivy v0.76.2 (the fetched page shows no year, and its footer reads 2026); whether the scan works offline with a downloaded database, and what a database update sends (neither the README nor the trivy.dev home page says, and the documentation was not reached).

#### 25. SARIF results reader

- **Status:** Missing. Nothing reads or writes the SARIF format, so the findings of a scanner (the secret scanner in security-tools 3.4, the image scan in item 14) have no shared shape to read.
- **Fits:** a JSON reader in `iConsole` for the `runs` and `results` (rule ID, level, message and locations), an F3 viewer for `*.sarif` and `*.sarif.json`, and a Markdown export through the shared report writer in security-tools 3.9. A result is one tool's finding and is shown in that tool's words, never as a verdict. The reader follows the errata's rules: a region needs a line, character or byte offset, and a relative location has no leading slash.
- **Sources:** the SARIF 2.1.0 Errata 01 (OASIS, 28 August 2023), which lists the corrections to the 2.1.0 standard and says the errata do not change its conformance requirements ([errata 01](https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/sarif-v2.1.0-errata01.html)). The OASIS standard approval date and the IANA media type `application/sarif+json` come from a search summary ([IANA media types](https://www.iana.org/assignments/media-types/application/sarif+json)).
- **Size:** not estimated in the survey. Effort note: a pure parser that the secret scanner and the image scan can share.
- **Not found:** `sarif`, `ruleId`, `static analysis result`.
- **Unverified:** the OASIS approval date (27 March 2020, search summary); the IANA media type entry; the licence of the errata (the page names OASIS's copyright and IPR policy and no licence for implementers).

### Hardening

#### 15. Windows application control status (App Control for Business and AppLocker)

- **Status:** Missing. Nothing reads whether an App Control policy or an AppLocker policy is applied on the machine.
- **Fits:** a read-only report, Windows only, beside the Windows permissions audit in security-tools 3.6, with "no policy found" stated as a fact and never as a verdict on the machine's security. It changes nothing.
- **Sources:** the Microsoft Learn page "App Control and AppLocker Overview", last updated 29 March 2026. App Control for Business controls which drivers and applications may run on a Windows client, applies to the whole computer, and is designed as a security feature under the MSRC servicing criteria. The page says AppLocker does not meet those criteria, and that customers who can use App Control should use it rather than AppLocker. It links a feature-availability page by Windows version ([overview](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/appcontrol-and-applocker-overview)).
- **Size:** not estimated in the survey. Effort note: read-only and Windows only.
- **Not found:** `applocker`, `wdac`, `selinux`, `apparmor`, `smart app control`, `code integrity`.
- **Unverified:** where the applied state is read from (no registry key or command was checked). The Linux equivalents, SELinux and AppArmor, were not checked in item 15; they are covered by item 31.

#### 31. Linux access-control status (SELinux and AppArmor), read-only

- **Status:** Missing. Nothing reads the SELinux mode or the AppArmor state.
- **Fits:** a read-only line in the Linux part of Properties or in the Unix permissions audit (security-tools 3.6), stated as the system's state, with "not installed" or "needs root" given as facts and never as a verdict on the machine. A tool run goes through `IExternalProcessRunner` with a fixed argument list, as the origin read does (file-operations.md), and never through a shell.
- **Sources:** the libselinux `security_getenforce` page, which returns 0 for permissive, 1 for enforcing and -1 on error, and is declared in `<selinux/selinux.h>`. It does not name the file it reads ([security_getenforce(3)](https://www.man7.org/linux/man-pages/man3/security_getenforce.3.html)). The AppArmor `aa-status` man page (version 3.1) says it needs root to read the loaded policy, and gives exit code 0 for enabled with a policy loaded, 1 for not enabled, 2 for enabled with no policy, 3 for missing control files, 4 for insufficient privilege and 42 for an internal error. It also has an `--enabled` option ([aa-status](https://apparmor.net/man/3.1/aa-status)).
- **Size:** not estimated in the survey. Effort note: read-only and small, with a fixed process call.
- **Not found:** `selinux`, `apparmor`, `getenforce`, `aa-status`, `libselinux`, `/sys/fs/selinux`.
- **Unverified:** the `/sys/fs/selinux/status` path (search summary only), and the behaviour on other distributions (not checked).

### Linux sandboxing

#### 21. Landlock restriction for a launched program (Linux only)

- **Status:** Partly. Commit `e5e95062` (#712) runs the shell session under `bwrap`. `iTerminal.Core/Services/Shell/SessionSandboxes.cs` binds the filesystem read-only (`--ro-bind / /`) and can unshare the network (`--unshare-net`), and the setting is `TerminalSandbox`, with default `none`. Commit `b3c33ef4` (#716) adds `ProcessContainerSandbox`, which runs through the MXC SDK. The same change makes the opt-in sandbox in security-tools 3.2 partly present, for the shell session only.
- **Still missing:** the Landlock part. The Landlock, `no_new_privs` and `prctl` terms are still not found, so the Landlock part of item 21 stands.
- **Earlier state:** at commit `3ba7401f` there was no Landlock, `no_new_privs`, `prctl`, bubblewrap or firejail call. The only program starts were the file launcher, the command line and the run-as path (file-operations.md).
- **Fits:** a small helper that the launcher starts through `IExternalProcessRunner`. The helper reads the ABI version and drops the rights the kernel does not know, as the kernel documentation advises. The restriction must be applied in the child before it runs the program: Landlock cannot be removed from the process that applies it, and children inherit it, so applied in iTerminal it would restrict the app itself. The dialog says what the restriction does not stop: the kernel page says `stat` and `chmod` cannot yet be restricted, and that pipes, sockets and namespace files under `/proc` are not restricted directly. Windows and macOS have no equivalent in this item.
- **Sources:** Landlock, the kernel's userspace documentation, described for kernel 7.4.0-rc3, a development version. It lists ABI versions 1 to 12 (ABI 1 from Linux 5.13), restricts file access, TCP ports (from ABI 4), UDP ports (ABI 10), scopes (ABI 6) and capabilities and namespaces (ABI 12), stacks up to 16 layers and passes the restriction to children ([Landlock](https://docs.kernel.org/userspace-api/landlock.html)).
- **Size:** not estimated in the survey. Effort note: the largest item in survey 3, Linux only, and it waits for the opt-in sandbox decision in security-tools section 7.
- **Not found (at commit `3ba7401f`):** `landlock`, `no_new_privs`, `prctl`, `bubblewrap`, `firejail`. `bubblewrap` now matches `SessionSandboxes`. `sandbox` matches only test seams and comments.
- **Unverified:** the licence of the kernel documentation, and which ABI version a given distribution kernel carries.

### Sanitisation

#### 23. Sanitisation category and record for a wipe

- **Status:** Partly. `SecureWipeService` has `WipePattern.Random`, `ThreePass` (zeros, ones, random, which the comment ties to the DoD 5220.22-M scheme) and `SevenPass` (`iTerminal.Core/Services/Wipe/SecureWipeService.cs`, lines 7 to 16). The wipe dialog states what an overwrite cannot reach (file-operations.md).
- **Missing:** a sanitisation category, in the terms NIST uses, stated for each run; and a record of which method was applied to which path, because the wipe leaves no report.
- **Fits:** a label in the wipe dialog's result (`SecureWipeDialog`), and an optional note in the evidence notebook through `IEvidenceNotebookService.AddTextAsync` (`iTerminal.Core/Services/Evidence/`), which already keeps notes and a SHA-256 per copied file (panels-and-drives.md). A note holds the paths and the method name, never the wiped contents.
- **Sources:** NIST SP 800-88 Revision 2, Guidelines for Media Sanitization, published September 2025 and superseding Revision 1 of 17 December 2014 ([NIST SP 800-88r2](https://csrc.nist.gov/pubs/sp/800/88/r2/final)). The CSRC page defines no categories.
- **Size:** not estimated in the survey. Effort note: reuses the wipe service and the evidence notebook, once the NIST categories are read from the PDF.
- **Not found:** `800-88`, `degauss`, `crypto erase`, `cryptographic erase`, `media sanitiz`. `purge` matches only the recycle bin's delete for good.
- **Unverified:** the category names Clear, Purge and Destroy, and the statement that degaussing is now a purge technique. Both come from search summaries, because the NIST PDF did not come through as text.

### Revocation and transparency

#### 19. Certificate Transparency inclusion

- **Status:** Missing. A certificate's logging state is not shown. The certificate reader does not look for embedded signed certificate timestamps, and nothing talks to a transparency log.
- **Fits:** a pure parser in `iConsole/Certificates` that lists the timestamps embedded in a certificate and the log IDs they name, read offline; and, as an opt-in step, a query to one log the user chooses, by leaf hash. A log does not answer a question about a domain name: its endpoints take an entry index, a tree size or a leaf hash. A "find every certificate for this domain" feature would therefore fetch entries and filter them on the client, or send the domain to a third-party index, which the rules in security-tools section 5 forbid by default. A result says that a log reports the entry, never that the certificate is trustworthy.
- **Sources:** RFC 9162, Certificate Transparency Version 2.0, Experimental, December 2021, obsoleting RFC 6962. It introduces the `transparency_info` TLS extension in place of `signed_certificate_timestamp`, and its log endpoints sit under `/ct/v2/` (`get-sth`, `get-proof-by-hash`, `get-entries` and others). The text names no lookup by domain, and says that monitors filter entries on the client ([RFC 9162](https://www.rfc-editor.org/rfc/rfc9162.html)).
- **Size:** not estimated in the survey. Effort note: an offline parse first; log queries wait for a decision on what they send.
- **Not found:** `certificate transparency`, `precertificate`, `signed certificate timestamp`, `transparency log`, `crt.sh`, `\bsct\b`.
- **Unverified:** the layout of the embedded timestamps inside a certificate, which the read part of the RFC did not cover; the current list of logs.

### Secrets

#### 16. Secret Service key for the password manager on Linux

- **Status:** Partly. `PasswordManager` seals its generated key and its verifier under the app's Data Protection keys, which on Windows are protected by the user's DPAPI. Its source comment says that elsewhere the keys are "only as private as the folder they are kept in" (`iTerminal.Core/Services/PasswordManager.cs`, `KeyProtector`).
- **Missing:** no call to the freedesktop Secret Service or to a macOS keychain, so on those systems the key sits in the app's folder.
- **Fits:** a platform store behind an `I*Service` in `iTerminal.Core`, asked for the key by `PasswordManager`, with a one-time move of an existing folder key into the store and no change to `passwords.sqlite`. Where the secret goes: the key is stored as one item in the user's keyring, labelled with the app's name and a purpose attribute; never in `Settings`, a URL, a log line or a command line (CLAUDE.md, credentials). A Secret Service prompt cannot be answered in a terminal, so the store must refuse and say how to unlock, not wait.
- **Sources:** the freedesktop Secret Service API, draft version 0.2. Collections and items each hold a secret, lookup attributes and a label. `Unlock` returns the objects it could unlock at once, and may return a prompt for the rest. The specification requires neither a master password nor access control ([specification](https://specifications.freedesktop.org/secret-service/latest-single)).
- **Size:** not estimated in the survey. Effort note: changes where a key lives, so it needs a migration and a Linux test host.
- **Not found:** `secret-service`, `libsecret`, `secret service`, `keychain`, `CredentialManager`, `CredRead`, `ProtectedData`. `keyring` matches only gpg's keyring in the signature checks.
- **Unverified:** the date of the Secret Service draft (the fetched page is dated 20 November 2026, and a search summary gave 8 April 2026 for the latest draft, so the date is unresolved); the status of an open issue that the API cannot unlock with a known secret without a prompt ([xdg-specs issue 74](https://gitlab.freedesktop.org/xdg/xdg-specs/-/issues/74)), which is unverified; the macOS keychain, which was not verified because the fetched Apple page gave only its title.

#### 30. Constant-time comparison for secret values

- **Status:** Partly. Keys and plaintext buffers are wiped with `CryptographicOperations.ZeroMemory` in `FileEncryptionService` and `PasswordManager` (for example `PasswordManager.cs`, around lines 113, 192 and 211).
- **Missing:** no `CryptographicOperations.FixedTimeEquals` call exists, so a future comparison of a token, a one-time code or a MAC would use `SequenceEqual` or `==`. The one comparison near a secret today is the password manager's verifier, which compares a decrypted constant after AES-GCM has authenticated it (`PasswordManager.cs`, lines 190 and 209), so no attacker-supplied value is compared.
- **Fits:** a helper beside a TOTP check (security-tools 3.4) or a token check in the remote drives, in `iTerminal.Core/Services`.
- **Sources:** the Microsoft Learn reference for `CryptographicOperations.FixedTimeEquals`, which says the comparison takes time that depends on the lengths and not the values, returns false early only for different lengths, and is fixed-time when both inputs are the same buffer; dated 1 July 2025, updated 1 July 2026, no obsolete notice ([FixedTimeEquals](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals)). Its sibling `ZeroMemory` is documented on the same class, and its remarks say it guards against optimisations that could drop writes, none of which is planned for .NET ([ZeroMemory](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.zeromemory)). The Microsoft page on `SecureString` advises against it for new development, so no item proposes it ([SecureString](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-security-securestring)).
- **Size:** not estimated in the survey. Effort note: a small helper, needed once a token or code is compared.
- **Not found:** `FixedTimeEquals`, `SecureString` (the only text match is `ConvertTo-SecureString` in `WindowsUserGroupService.cs`, a PowerShell string).

### Authentication

#### 32. Passkey unlock for the password manager

- **Status:** Missing. No WebAuthn, passkey, FIDO or CTAP term appears, and the vault unlocks with the master secret only (the sealed key and the verifier in `PasswordManager.cs`).
- **Fits:** a second unlock method beside the master secret, which stays as the recovery path, behind an `I*Service` in `iTerminal.Core/Services`. The console app has no browser, so the client side would need a native bridge to a platform authenticator. That bridge was not researched, and this item does not propose sealing the vault key with an authenticator.
- **Sources:** the W3C Web Authentication Level 3 Recommendation, published 25 August 2026, with six editors listed and a W3C copyright notice. It succeeds Level 2 (2021) and describes an API for web pages, so its text does not describe a console program ([WebAuthn Level 3](https://www.w3.org/TR/webauthn-3/); the first part of the text was read).
- **Size:** not estimated in the survey. Effort note: needs a native authenticator bridge, so research comes first.
- **Not found:** `webauthn`, `passkey`, `fido`, `ctap`, `authenticator`.
- **Unverified:** that the page has had no substantive change since the 26 May 2026 Candidate Recommendation (search summary); whether an authenticator can derive a key (the PRF or hmac-secret extensions), which was not read.

### Headers

#### 20. HTTP security-header grade

- **Status:** Partly. The HTTP headers tool (`NetworkToolService.HttpHeadersAsync`, `iTerminal.Core/Services/NetworkTools/NetworkToolService.cs`) sends a HEAD request and writes every response header it receives, with no judgement.
- **Missing:** a grader over those headers. For each recommended header (Strict-Transport-Security, Content-Security-Policy, X-Content-Type-Options, Referrer-Policy, the cross-origin policies and Permissions-Policy) it would say present, absent or weak; and it would flag Server and X-Powered-By as headers to remove, and Expect-CT and Public-Key-Pins as deprecated.
- **Fits:** a pure function in `iConsole` over the header lines the tool already holds, shown in the same dialog. A result is a hint about one response, never a verdict on the site. Its HSTS line states the cost as well: a long max-age that the certificate cannot back locks users out.
- **Sources:** the OWASP HTTP Headers Cheat Sheet, which lists the recommended headers, the headers to remove, the deprecated ones and the HSTS guidance; the page carries no date ([HTTP Headers Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html)). RFC 6797, HTTP Strict Transport Security, Proposed Standard, November 2012 ([RFC 6797](https://www.rfc-editor.org/info/rfc6797)). Content Security Policy Level 3, a W3C Working Draft dated 27 October 2026, not yet a Recommendation ([CSP Level 3](https://www.w3.org/TR/CSP3/)).
- **Size:** not estimated in the survey. Effort note: a pure function over headers the tool already reads.
- **Not found:** `strict-transport-security`, `content-security-policy`, `x-content-type-options`, `x-frame-options`, `hsts`.
- **Related:** Expect-CT and Public-Key-Pins are deprecated, so they appear only as hints here.

## Suggested order

The surveys ranked their own items, and the rankings are not merged into one list. Each list is in the order its survey gave, with the reason it gave.

### Survey 1 ranking

1. Shortcut (.lnk) inspector (item 5): a pure parser, reachable from F3, and it shows a target without running it.
2. CycloneDX SBOM reader (item 4): a pure parser, and its component list feeds the package check in security-tools 3.6.
3. BLAKE3 digest (item 1): a small change to the checksum enum, once a maintained implementation is chosen.
4. ML-DSA signatures (item 3): one more algorithm in the in-process format, with the platform API already in .NET 11.
5. Argon2id key derivation (item 2): needs a package and bounds in the header.
6. C2PA content credentials viewer (item 6): needs a native-library decision.
7. RFC 3161 timestamp check (item 7): needs an opt-in network call and the ASN.1 reader.
8. Sigma rules over event logs (item 8): depends on the event-log reader in security-tools 3.7.

### Survey 2 ranking

1. Git commit signature status (item 10): one format string and a model field, with no new dependency.
2. Authentication-Results reader (item 13): a pure parser beside the existing `.eml` reader.
3. Look-alike file names (item 12): extends an existing pure check.
4. Application control status (item 15): read-only and Windows only.
5. in-toto attestation check (item 9): a new parser and a digest check. The format is still under development.
6. Known-good file set (item 17): waits on the NSRL terms.
7. Container image scan (item 14): a runtime dependency and a network database. The maintainer picks the tool.
8. Secret Service key (item 16): changes where a key lives, so it needs a migration and a Linux test host.
9. PDF signature inspector (item 11): waits on a readable copy of the PAdES text.

### Survey 3 ranking

1. HTTP security-header grade (item 20): a pure function over headers the tool already reads.
2. OpenPGP signature packet reader (item 22): a pure parser that names the signer without gpg.
3. Certificate revocation status (item 18): an opt-in network call, built on the viewer's certificate reader.
4. fs-verity status (item 24): a read-only Linux row through a libc call the app already makes elsewhere.
5. Sanitisation category and record (item 23): reuses the wipe service and the evidence notebook, once the NIST categories are read from the PDF.
6. Certificate Transparency inclusion (item 19): an offline parse first; log queries wait for a decision on what they send.
7. Landlock restriction (item 21): the largest item, Linux only, and it waits for the opt-in sandbox decision in security-tools section 7.

### Survey 4 ranking

1. SARIF results reader (item 25): a pure parser that the secret scanner and the image scan can share.
2. OpenVEX statement reader (item 26): a pure parser beside the CycloneDX reader (item 4), with the same test corpus idea.
3. Linux access-control status (item 31): read-only and small, with a fixed process call.
4. Constant-time comparison (item 30): a small helper, needed once a token or code is compared.
5. Fuzzy hash (item 28): a pure function, after a licence choice between Apache-2.0 and BSD-3-Clause and a test corpus.
6. TUF signed metadata (item 27): the largest trust decision here, needing a key bootstrap and rollback rules.
7. Folder vault (item 29): a new drive scheme and format, so a large change.
8. Passkey unlock (item 32): needs a native authenticator bridge, so research comes first.

## Considered and not added

- Certificate Transparency log search: now item 19.
- HTTP security-header grader: now item 20.
- Kernel sandboxes such as Landlock: now item 21.
- Encrypted folder drive (a Cryptomator-style vault): not in survey 2 as a numbered item; researched in survey 4 as item 29.
- WebAuthn, passkeys and FIDO2 for the vault: now item 32. A platform authenticator bridge is not researched.
- ML-KEM: not researched. It belongs with item 3.
- Sigstore Rekor transparency log lookup: not researched. The `.bundle` format is in security-tools section 2, and item 6 covers a different format.
- Expect-CT and Public-Key-Pins: deprecated, so they appear only as hints in item 20.
- ssdeep: not checked, because the search returned nothing on it. TLSH (item 28) is the candidate.
- SecureString: advised against by its documentation, so it is not proposed.
- Other SARIF producers and consumers: not surveyed. The format is the only fixed point in item 25.

## Already covered (no new item)

- YARA rule scanning: security-tools 3.2. A newer YARA implementation was not checked.
- ClamAV behind the malware-scan interface: security-tools 3.2.
- Package vulnerability check through OSV: security-tools 3.6. Item 14 is a scanner over images and may be merged with it.
- age interop: security-tools section 4 and 3.4.
- TOTP, breached-password check, SSH key inspector, JWT decoder, secret scanner: security-tools 3.4. Item 25 gives the secret scanner a format.
- Argon2 verification of stored hashes: security-tools 3.5. Item 2 is a different use.
- Windows event log viewer and authentication log summary: security-tools 3.7. Item 8 depends on them.
- TLS endpoint inspector and certificate expiry watch: security-tools 3.3. Item 18 adds revocation to the same inspector.
- DNS, SPF, DKIM and DMARC record checks: security-tools 3.3. Item 13 reads the receiver's recorded result instead.
- Windows permissions audit: security-tools 3.6. Item 15 is a different control.
- Publisher signatures with sigstore `.bundle` files and cosign files: security-tools section 2 (Have), reported as not checked. Item 6 is a different format.
- Hash manifests: security-tools section 2 (Have).
- SPDX SBOM reader: item 4 records that SPDX was not checked there.
- Git and tag signatures: item 10. Item 27 is a different trust chain for updates.
- Authenticode revocation of update files: present (`AuthenticodeVerifier`). Item 18 covers other certificates.
- Shell-session sandbox (bubblewrap): item 21 and security-tools 3.2.
- Binary hardening flags (RELRO, BIND_NOW, the PE DLL characteristics): security-tools section 2 (Executable analysis), `iTerminal.Core/Services/Executables/ElfAnalyzer.cs`.
- Evidence bundle: security-tools 3.1. Partly present through the evidence notebook (`iTerminal.Core/Services/Evidence/`, `evidence://`), which keeps a SHA-256 per entry, a verify command and a read-only drive. security-tools.md is read-only, so its section 3.1 is out of date and the maintainer should update it.
- Lynis and other Linux hardening audits: security-tools 3.6 (Unix permissions audit and benchmark-style report). Not researched in survey 3.
- gitleaks and other secret scanners: security-tools 3.4. Not researched in survey 3.
- OSV package check and Trivy image scan: security-tools 3.6 and item 14.
- Bidi and hidden-character file names: present in `FileDisguise`. Item 12 extends it.

## Sources surveyed

- Survey 1, 2026-11-20. Topics: hash functions, key derivation, signatures, shortcut files, provenance, timestamps and event-log rules. Items 1 to 8. Basis: official documentation (standards, project repositories and release pages, and vendor documentation), not run-time behaviour.
- Survey 2, 2026-11-20. Topics: attestations, git and PDF signatures, look-alike names, mail authentication results, image scanning, application control, keystores and known-file sets. Items 9 to 17. Same basis.
- Survey 3, 2026-11-20. Topics: certificate revocation and transparency, header grading, Linux sandboxing and file integrity, OpenPGP packets and media sanitisation. Items 18 to 24. Basis: RFCs, a W3C draft, the Linux kernel documentation, a NIST publication page, Microsoft Learn and the OWASP cheat sheet.
- Survey 4, 2026-11-20. Topics: source-format standards, fuzzy hashes, VEX statements, signed update metadata, folder vaults, constant-time comparison, Linux access-control status and passkeys. Items 25 to 32. Basis: the OASIS SARIF errata, the OpenVEX and TUF specification repositories, the TLSH repository and its licence file, the Cryptomator security documentation, Microsoft Learn API references, the libselinux and AppArmor man pages and the W3C Web Authentication Level 3 Recommendation.

Each survey's "Not found" entries are case-insensitive greps of `iConsole/` and `iTerminal.Core/` (`*.cs`) at the commit named in its item.

## Consolidated

Consolidated on 2026-11-20 from the four surveys and their status blocks. Status changes applied:

- Item 7 (RFC 3161 time stamp check): Missing to Partly. Commit `f283477a` (#703) stamps evidence entries.
- Item 11 (PDF signature inspector): Missing to Partly. Commit `64c93224` (#692) adds `PdfSignatureReader`.
- Item 21 (Landlock, bubblewrap and firejail): Missing to Partly. Commit `e5e95062` (#712) adds the bwrap shell sandbox. The Landlock part is still missing.
- Item 4 (CycloneDX): still Missing as a reader. Commit `4ad0edba` (#717) adds a writer.
- Item 5 (.lnk inspector): still Missing. Commit `265f9aee` adds a sample file only.
- Item 22 (OpenPGP signature packet reader): Partly to Built. This change adds `iConsole/OpenPgp/OpenPgpSignatureReader.cs`, shows the first packet of a gpg signature in `SignatureDialog` (`SignatureVerdict.Packet`), and records the facts in file-operations.md. Key lookup and trust stay out of scope by design.
- Items 1, 2, 3, 6, 8, 9, 10, 13 to 20 and 23 to 24 were re-checked against the source on the 70fe8ad5 pass (key terms grepped in `iConsole/` and `iTerminal.Core/`) and still match. Item 12 was not re-checked in the latest pull. Item 21 was checked on the same pass: the Landlock part is still missing, and the bwrap and MXC sandboxes are for the shell session only.

Totals: 32 items. Partly: 7, 11, 12, 13, 16, 18, 20, 21, 23, 27, 30 (11 items). Built: 22 (1 item). Missing: the other 20.

## Open issue for the maintainer

The RFC 3161 time stamp is on by default, which conflicts with the opt-in rule in security-tools.md section 5, rule 8 ("No default network use. Each outside call is opt-in, described before it is made, and limited to the minimum data").

- `iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs` (line 381) registers `Rfc3161TimestampAuthority` as the `ITimestampAuthority` singleton, with no setting check.
- `iTerminal.Core/Services/Evidence/EvidenceNotebookService.cs` calls `StampAsync` for each entry (lines 153 and 332), and `StampAsync` (line 349) calls the authority whenever one is registered. There is no opt-in check and no dialog that describes the request before it is sent.
- `iTerminal.Core/Services/Timestamps/ITimestampAuthority.cs` uses `DefaultAuthority = "https://freetsa.org/tsr"` when the `EvidenceNotebook.TimestampAuthority` setting has never been set. An empty setting turns stamping off, which is the only off switch found. Only the SHA-256 digest is sent, as the code comment says.

The source matches the claim. The registration is in `iTerminal.Core/Application/`, not `iTerminal/`. Nothing in the source, the tests or the dialogs gates the call on opt-in. This section records the finding only; the source and `Documentation/security-tools.md` were not changed.

## Later surveys

### Survey 5: SSH-signed files, installed-file verification, boot state, SPDX, DNSSEC status and Sigstore bundles

Surveyed on 2026-11-21 on branch `fix-application-lifetime` at commit `38ed0b4b`. Topics: SSH signatures, the SPDX document format, installed-file verification, kernel lockdown and Secure Boot, the DNSSEC authenticated-data flag and Sigstore bundles. Basis: official documentation (the OpenSSH `PROTOCOL.sshsig` file, the SPDX specification and releases page, the rpm and debsums manual pages, the kernel lockdown man page, RFC 4035, the Sigstore bundle page and the OpenSCAP project page), not run-time behaviour. "Not found" means a case-insensitive grep of `iConsole/`, `iTerminal.Core/` and `iConsole.Formats/` (the widened scope is needed because commit `84415ff3` moved the format readers into `iConsole.Formats/`). The sections of this survey sit under `## Later surveys` so that the heading levels stay in order.

**Signatures**

33. **SSHSIG signature check (`ssh-keygen -Y`).**
    - Missing: no verifier reads a file armoured as `-----BEGIN SSH SIGNATURE-----`. `PublisherSignatureService.PickTool` names gpg, minisign and Sigstore, and reads the head of a `.sig` file only to tell this program's own format from the others.
    - Fits: a new `PublisherTool` value chosen in `PickTool` by the armour line, run through `IExternalProcessRunner` with a fixed argument list, and checked against an allowed-signers file the user names. The key is never taken from beside the signed file, as the keyring rule in file-operations.md already requires. A check that does not identify the signer must say so in its verdict.
    - Seen in: OpenSSH `PROTOCOL.sshsig`, revision 1.5 as read on GitHub, with the OpenBSD tag dated 10 October 2026 ([PROTOCOL.sshsig](https://github.com/openssh/openssh-portable/blob/master/PROTOCOL.sshsig)). It defines the armour lines, a blob that starts with the magic `SSHSIG`, a version that a verifier must reject when newer than it supports, the signer's key, a namespace, a reserved field, a hash name (sha256 or sha512 only) and a signature. The signed data is a wrapper of the magic, namespace, reserved field, hash name and message hash.
    - Not found: `sshsig`, `SSH SIGNATURE`, `allowed_signers`, `ssh-keygen`.
    - Unverified: the `ssh-keygen -Y verify` arguments and the `check-novalidate` variant (from a search summary; the man page was not read); how a `.sig` in this format is reported today (not run).

34. **SPDX document reader.**
    - Missing: nothing reads an SPDX document. Item 4 records that SPDX was not checked, and this item is the SPDX side of the same reader.
    - Fits: a parser in `iConsole.Formats` beside the CycloneDX reader of item 4, giving the component list to the package check in security-tools 3.6. The reader must read the version first, because SPDX 2.x and 3.x are different models.
    - Seen in: the SPDX 3.0.1 specification introduction, which describes SPDX as a data exchange format and lists the model's profiles (Core, Software, Security, Licensing, Dataset, AI, Build, Lite and Extension) ([SPDX 3.0.1](https://spdx.github.io/spdx-spec/v3.0.1/front/introduction)). The releases page marks 3.0.1 as Latest (17 December, year not shown), lists v3.1-RC1 as a pre-release (24 January, year not shown) and dates 2.3 to 3 November ([releases](https://github.com/spdx/spdx-spec/releases)).
    - Not found: `spdx`, `SPDXID`, `spdxVersion`.
    - Unverified: the serialisations (the page names none; JSON-LD comes from a search summary); the years of the 3.0.1 and v3.1-RC1 releases; whether 3.1 is final, since only the pre-release is on the page.

**Hardening**

35. **Kernel lockdown and Secure Boot state (Linux, read-only).**
    - Missing: nothing reads whether the kernel is in lockdown or whether Secure Boot is on. `lockdown` matches only the iOS device lockdown in `iTerminal.Core/Services/IPhoneDeviceService.cs`.
    - Fits: a read-only line beside the SELinux and AppArmor status of item 31, stated as the system's state, with "not available" given as a fact. A Secure Boot tool, if one is used, runs through `IExternalProcessRunner` with a fixed argument list and never through a shell.
    - Seen in: the `kernel_lockdown(7)` man page, man-pages 6.19 ([kernel_lockdown(7)](https://man7.org/linux/man-pages/man7/kernel_lockdown.7.html)). Lockdown was added in Linux 5.4, needs `CONFIG_SECURITY_LOCKDOWN_LSM` and `lockdown` in the `lsm=` parameter, blocks `/dev/mem`, `/dev/kmem`, `/dev/kcore`, BPF and kprobes, and requires modules to be validly signed.
    - Not found: `lockdown` (the one hit is the iOS device service), `secureboot`, `secure_boot`, `mokutil`.
    - Unverified: the names of the integrity and confidentiality modes and the sysfs file that shows the active one (a search summary only, since the fetched man page does not describe them); the Secure Boot command (`mokutil` not checked); the Windows Secure Boot and TPM state (not researched).

36. **Installed-file verification against the package record (`rpm --verify`, `debsums`).**
    - Missing: nothing compares an installed file with the package that owns it. `md5sums` matches only a comment on the hash-manifest file names in `iTerminal.Core/Services/Checksums/HashManifest.cs`.
    - Fits: a read-only action on a package in `PackageManagerDialog` (Alt+F8), run through `PackageManagerProcess`, and a line in Properties (Ctrl+F12) for a file that a package owns. The output lists each changed attribute as a fact. A changed file is not a verdict of compromise, so the dialog should point to the file-integrity monitor of security-tools 3.7 for that purpose.
    - Seen in: the debsums manual page, from the Ubuntu 22.04 copy of debsums 3.0.2 ([debsums(1)](https://manpages.ubuntu.com/manpages/jammy/man1/debsums.1.html)). It checks installed files against `/var/lib/dpkg/info/*.md5sums`, has the options `-c`, `-s`, `-l` and `-a`, returns exit codes 0 to 3 and 255, and calls its value as a security tool limited. The rpm manual, rpm 6.1.0 dated 20 August 2026 ([rpm(8)](https://rpm.org/docs/latest/man/rpm.8)), says verify compares size, digest, mode, file type, owner, group, time and capabilities with the database, prints one character per test and prints a question mark when a test could not run.
    - Not found: `debsums`, `rpm -V`, `rpm --verify`, `dpkg -V`, `md5sums` (the one hit is a comment).
    - Unverified: the `rpm -Vp` form for a package file (the page does not document it); whether `dpkg --verify` is available to the apt service (not checked).

**Network**

37. **DNSSEC authenticated-data flag in the DNS tool.**
    - Missing: the DNS lookup uses `Dns.GetHostEntryAsync` (`iTerminal.Core/Services/NetworkTools/NetworkToolService.cs`, lines 144, 165 and 408), which returns addresses and no authenticated-data flag. Nothing reads DNSSEC state.
    - Fits: a query path in `NetworkToolService` that sends one DNS query, which asks for DNSSEC data, to a resolver the user names, and shows whether that resolver set the flag. The verdict must say that the flag is the resolver's claim, as RFC 4035 requires for a stub. A check of the chain itself is a separate and larger item.
    - Seen in: RFC 4035 ([RFC 4035](https://www.rfc-editor.org/rfc/rfc4035.html)), sections 3.2.3 and 4.9.3, read in part. A recursive server sets the flag only when it treats the whole answer as authentic, and a non-validating stub must not rely on it unless it got the answer from a trusted recursive server over a secure channel.
    - Not found: `dnssec`, `authenticated data`, `\bAD bit\b`, `DnsQuery`.
    - Unverified: the validation procedure in section 5 (not read); the later updates named on the search page (RFC 4470, 6014, 6840, 8198, 9077 and 9520), not read.

**Signatures (Sigstore)**

38. **Sigstore bundle check (offline).**
    - Missing: a `.bundle` file is routed to `PublisherTool.Sigstore` in `PublisherSignatureService.PickTool`, and `VerifyOneAsync` returns `Unchecked` for that tool. Nothing reads a bundle's certificate, log entry or timestamp. security-tools section 2 lists `.bundle` as checked with gpg or minisign, which the code does not do, so that row needs a correction by the maintainer.
    - Fits: a verifier in `iTerminal.Core/Services/Checksums` behind the existing Sigstore branch, which compares the bundle's signed digest with the file's SHA-256. The trusted roots and the expected signer identity come from a file the user sets, never from the bundle itself or from a URL, and a verdict names the identity that matched.
    - Seen in: the Sigstore bundle format page, version 0.3.2 dated 14 January 2025 ([bundle](https://docs.sigstore.dev/about/bundle/)). The verification material holds a certificate or key hint, log entries and RFC 3161 timestamps. The content is a message signature or one DSSE envelope signature. A log entry holds an inclusion promise or an inclusion proof with a checkpoint, and the page describes no offline procedure.
    - Not found: `rekor`, `fulcio`, `inclusion proof`. The words `sigstore` and `cosign` match only the Unchecked branch and its label in `iTerminal.Core/Services/Checksums/PublisherSignatureService.cs` and `iTerminal.Core/Dialogs/ChecksumDialog.cs`.
    - Unverified: whether an offline check is possible (the page gives no procedure; the protobuf schema and the checkpoint rules come from search summaries); the Rekor v2 and cosign version notes (search summary only).

**Suggested order**

These are the new items only, ranked by value to iTerminal.

1. SSHSIG check (33): one fixed process call and a file the user already owns, so it is small.
2. Installed-file verification (36): read-only, and it reuses the package-manager dialog.
3. Kernel lockdown and Secure Boot state (35): read-only, one status line beside item 31.
4. SPDX reader (34): a pure parser beside item 4.
5. DNSSEC flag in the DNS tool (37): needs a DNS query path and an honest label.
6. Sigstore bundle check (38): needs a trust-root decision, and the source gives no offline procedure.

**Already covered, or considered and not added**

- OpenSCAP: a tool option for the benchmark-style report in security-tools 3.6, so no new item. The project page names the LGPL-2.1 licence, `oscap` subcommands for XCCDF, OVAL and SCAP source streams, and says Windows support has been void since 1 February 2022. It shows no release section; a Debian package name for 1.4.3 comes from a search summary and is unverified.
- LUKS (Linux) and FileVault (macOS) encryption status: not added. The search did not confirm a read-only status command, and the cryptsetup `luksDump` page was not read in full. Windows BitLocker is read by the disk manager (file-operations.md, drive actions), so the Windows part is present.
- signify (OpenBSD): not researched. A second `.sig` tool would fit `PublisherTool`, so it is a candidate for a later pass.
- Lynis: not researched in this pass; security-tools 3.6 covers the audit it does.
- ML-KEM: item 3. Rekor transparency log lookup: items 19 and 38.
- gitleaks: security-tools 3.4. YARA (including newer versions), ClamAV: security-tools 3.2. osv-scanner and Trivy: security-tools 3.6 and item 14.
- Windows Secure Boot and TPM state: not researched; the Windows part of item 35 is unverified.

#### Status after the pull of 2026-11-21

- Item 30 (constant-time comparison): still Partly, but its "Missing" text is out of date. `CryptographicOperations.FixedTimeEquals` is now called once, in `TokenMatches` in `iTerminal.Core/Services/Sharing/FileShareRequestHandler.cs` (line 145, from commit `91d554b4`, #731), to compare a share token. No shared helper exists.
- Items 11 and 18, and the ElfAnalyzer path in the binary-hardening line of "Already covered": paths moved by commit `84415ff3` (#802). `PdfSignatureReader` is now `iConsole.Formats/Pdf/PdfSignatureReader.cs`, `CertificateReader` is `iConsole.Formats/Certificates/CertificateReader.cs`, and `ElfAnalyzer` is `iConsole.Formats/Executables/ElfAnalyzer.cs`. Status unchanged.
- Item 13: the `.eml` reader is now `iConsole.Formats/Services/MimeMessageReader.cs`. Status unchanged (Partly); no `Authentication-Results` term matches.
- Item 7: still Partly. The open issue (default-on RFC 3161 stamping) still matches the source: the registration is in `iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs` (line 381), the default authority is `https://freetsa.org/tsr` in `ITimestampAuthority.cs`, and `EvidenceNotebookService.cs` stamps at lines 153 and 332, with no opt-in check.
- Items 10 and 12 were re-checked by term sweep: item 10 (`%G?`, `%GS`, `verify-commit`) still Missing, and item 12 (confusable, homoglyph, mixed-script) still Partly with the mixed-script part missing. Item 12 was not re-checked in the last pull.
- Items 1, 2, 3, 4, 5, 6, 8, 9, 14, 15, 16, 17, 19, 20, 21, 24, 25, 26, 27, 28, 29, 31 and 32 were re-checked by term sweep and still match: no term of their "Not found" lists is in the source. Item 4 is still a writer only, item 5 still has no shell-link reader, and item 21 still has no Landlock call.
- Totals after this survey: 38 items. Partly: 11 (7, 11, 12, 13, 16, 18, 20, 21, 23, 27, 30). Built: 1 (22). Missing: 26.
