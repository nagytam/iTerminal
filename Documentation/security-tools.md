# Security features and tools for iTerminal

This document surveys what iTerminal already offers for security work, then lists the features and tools that could be
added. It is a proposal: an item marked **Have** exists in the code, and everything else is **Proposed** and unbuilt. Each
proposal names where it would live, so the next step can be checked against the architecture docs before any code is
written.

## 1. Scope and stance

iTerminal is a dual-pane file manager and terminal. Its security value comes from the things a person handling files, hosts
and secrets does every day: checking that a download is what it claims to be, looking inside a suspicious file without
running it, auditing machines they run, keeping secrets out of the wrong places, and recording what they did.

**In scope** (defensive, administrative, forensic-style, education, CTF):

- Inspecting, hashing, verifying and sanitising files, including untrusted ones.
- Auditing and troubleshooting hosts and networks the user administers.
- Handling credentials, keys and certificates safely.
- Recording evidence and actions with integrity.
- Learning and CTF-style analysis of encodings, binaries and captures.

**Out of scope** (declined, whatever the stated purpose):

- Exploitation frameworks, command-and-control, payload generation.
- Password cracking or credential spraying against accounts, and brute force of anything the user does not own.
- Evasion of anti-malware or EDR, or tampering with another party's logs or traffic.
- Interception of traffic the user is not party to (no ARP poisoning, no MITM proxies for other people's devices).
- Telemetry or default-on lookups that send file contents or hosts off the machine.

**Active network features** (the subnet scan that already exists, port checks, banner grabs) keep an explicit target
confirmation and a rate ceiling. A scope file (section 5.3) can list the targets the user is authorised to touch, and a
target outside it asks first.

**Data leaving the machine.** Any feature that calls an outside service sends the least it can: a SHA-256, a five-character
hash prefix, or a package name and version. Never file contents, never a whole log, and never on opening a dialog. This is
how the existing VirusTotal lookup already works (see `Documentation/architecture/file-operations.md`).

**Where code goes.** The architecture rules apply unchanged (`CLAUDE.md`):

- Pure parsers and analysers with no app state (entropy, magic numbers, JWT, ASN.1, timestamp conversion, the transform
  steps of a recipe) belong in `iConsole`, with unit tests in `iConsole.Test`.
- Behaviour, dialogs, services and drives belong in `iTerminal.Core`.
- External programs (YARA, ClamAV, nmap, tshark, OpenSSL, GnuPG, age, osv-scanner, testssl.sh) follow the runtime-dependency
  pattern in `Documentation/architecture/runtime-dependencies.md`: installed on demand, never shipped, and absent means a clear
  message, not an error.
- Anything file-shaped that a user browses (evidence bundles, captures, certificate stores) can get a URL scheme, following
  the checklist in `Documentation/architecture/panels-and-drives.md`.

## 2. What iTerminal already has

| Capability | What it does | Where to reach it | Code |
| --- | --- | --- | --- |
| **Have** Checksums | MD5, SHA-1, SHA-256 and SHA-512 in one read; a typed checksum is compared as it is typed; `name.sha256` and `SHA256SUMS` files are checked without being asked | Ctrl+F12 is properties; checksum dialog Shift+F12 | `Services/Checksums/ChecksumService.cs` |
| **Have** Transfer verification | A copy's hash is compared with its source | Copy pipeline | `Services/Checksums/TransferVerifier.cs` |
| **Have** Publisher signatures | `.asc`, `.sig`, `.gpg`, `.minisig`, `.bundle` checked with `gpg --verify` or `minisign -V` | Ctrl+Shift+F7 | `Services/Signing/SignatureService.cs` |
| **Have** Hash reputation | SHA-256 looked up on VirusTotal on request; only the hash leaves the machine | F4 in the checksum dialog | `Services/Reputation/` |
| **Have** Anti-malware scan | Microsoft Defender custom scan of a file or folder (Windows) | Scan with Defender command | `Services/Defender/DefenderScanService.cs`, behind `IMalwareScanService` |
| **Have** Secure wipe | Overwrite then delete marked files and folders | Ctrl+Shift+F8 | `Services/Wipe/SecureWipeService.cs` |
| **Have** File encryption | AES-256-GCM with PBKDF2 keys, `.enc` files | Ctrl+Shift+E | `Services/Encryption/FileEncryptionService.cs` |
| **Have** Password manager | Encrypted vault for credentials, used by the remote drives and the cloud drives | Ctrl+X | `Services/PasswordManager.cs`, `Services/PasswordCrypto.cs`, `Data/VaultEntry.cs` |
| **Have** Host identity | SSH host keys (SHA-256) and FTPS certificates are trusted on first use and refused when they change | `known_hosts.json` in the data folder | `Services/TrustedHostStore.cs` |
| **Have** Network tools | Ping, traceroute, DNS and reverse DNS, whois, TCP port check, HTTP headers, subnet scan | Network tools dialog | `Services/NetworkTools/` |
| **Have** Executable analysis | PE, ELF and Mach-O headers, section entropy and packer hints, Authenticode certificates | Viewer on a binary (F3) | `Services/Executables/` |
| **Have** Disassembly | Native x86/x64 linear sweep (Iced) and managed IL | Viewer on a binary or assembly | `iConsole.Formats/Services/Disassembly/` |
| **Have** Hex view | Hex mode in the F3 viewer, toggled with F4, with go-to offset | F3, then F4 | `iConsole/Controls/HexViewer.cs` |
| **Have** Binary diff | Byte comparison of two files | Compare dialogs | `Dialogs/BinaryDiffView.cs` |
| **Have** File disguise check | Magic bytes compared with the extension | Automatic note | `Services/Security/FileDisguiseChecker.cs` |
| **Have** Certificate viewer | X.509 files (PEM, DER, PKCS#7 and others) | F3 | `iConsole/Certificates/` |
| **Have** Metadata strip | Removes EXIF and document metadata from copies | Strip metadata dialog | `Dialogs/StripMetadataDialog.cs` |
| **Have** Alternate data streams | Lists and reads NTFS streams, including `Zone.Identifier` | Streams dialog | `Services/Streams/` |
| **Have** Access control | Windows ACLs and Unix permission editing | Access control dialog | `Services/AccessControl/` |
| **Have** Users and groups | Local accounts and groups on both platforms | Users and groups dialog | `Services/UsersAndGroups/` |
| **Have** Elevation | Restart as administrator or root, or run one program elevated | Ctrl+Shift+A, Ctrl+Shift+R | `Services/Elevation/` |
| **Have** Process and network managers | Running processes and network connections | Task and network manager dialogs | `Dialogs/ProcessManagerDialog.cs`, `Dialogs/NetworkManagerDialog.cs` |
| **Have** Hash manifest | Writes and reads a list of file hashes for a folder | Hash manifest dialog | `Dialogs/HashManifestDialog.cs` |
| **Have** Duplicate finder | Identical files found by size, then partial and full SHA-256 | Ctrl+Shift+F7 family | `Services/Duplicates/` |
| **Have** HTTP archive and certificate views | HAR files and certificate details | HAR viewer, F3 | `iConsole/Har/`, `Dialogs/HarViewerDialog.cs` |
| **Have** Log highlighting | Colour for log files | F3 viewer | `iConsole/Helper/LogHighlighting.cs` |
| **Have** Recycle bin and Trash | Restore and delete-for-good through the real bin | Ctrl+F8 drive | `Model/ContentModels/ContentModelRecycleDrive.cs` |
| **Have** Link-safe deletes | A recursive delete never walks through a symbolic link into another folder | F8 | `Documentation/architecture/file-operations.md` |
| **Have** Sandboxed reading | Archives are read with 7-Zip's stdin closed; previews never execute; files open with a named program, never through a shell | Panels, F3, Shift+F3 | `Documentation/architecture/panels-and-drives.md`, `Documentation/architecture/file-operations.md` |
| **Have** Remote drives | SSH, SFTP, SCP, FTP and FTPS, WebDAV, SMB, S3 (hand-signed SigV4), OneDrive, Google Drive | Drive URL schemes | `Documentation/architecture/panels-and-drives.md` |
| **Have** AI assistant tools | The AI bar can act on the panels (copy, compare, read) with approval for actions | Alt+F1 and AI bar | `AI/AiPanelTools.cs`, `Dialogs/AiActionApprovalDialog.cs` |

The gaps below are things the app does not do yet. Several are small extensions of what is already built.

## 3. Proposed features by area

Each entry gives the feature, why it helps, where it would live, and any risk. Items are grouped by the job they serve.

### 3.1 Evidence handling and chain of custody

- **Evidence bundle.** Select files; record for each its path, size, SHA-256, timestamps, permissions and the operator's
  name and time; export a ZIP with a manifest. *Where:* a dialog plus a service in `iTerminal.Core`, writing through
  `ZipArchive`. *Note:* builds on the hash manifest and the checksum service.
- **Chain-of-custody log.** An append-only, hash-chained log of what the app did to a set of files (hashed, copied, moved,
  wiped). Each entry carries the hash of the previous one, so an edited or deleted line breaks the chain. *Where:*
  a SQLite table through a new `I*Service` (not `Settings`, which is the cache-replaced table). *Note:* the entries must
  not record file contents.
- **Read-only panel mode.** A panel flag that refuses every write (copy-out is allowed). This keeps evidence from being
  changed by a stray F8. *Where:* `ContentBrowser` plus the file-operation gate. Destructive actions already ask first;
  this adds a hard refusal.
- **Acquisition verification.** Hash the source and the copy and report a match or a mismatch in the transfer log. Largely
  **Have** through `TransferVerifier`; what is missing is a visible record of the result. *Where:* the copy pipeline.
- **Forensic timeline.** The modified, accessed, created and changed times of every file under a folder, merged into one
  sorted view or exported as CSV. *Where:* a `FileInformationRefreshTask`-style background task (a folder walk, so it must
  not run on a key handler). *Note:* created and changed times differ by platform. Say which is which in the output.
- **Disk or partition imaging with a hash.** Confirm whether `DiskImageDialog` already records a hash of the image. If it
  does not, add one.

### 3.2 Malware triage and safe handling of untrusted files

- **ClamAV scanning.** A second `IMalwareScanService` implementation over `clamscan`, installed on demand as a runtime
  dependency. This is the cheapest cross-platform scanner, and the interface already exists (`IMalwareScanService`,
  `ScanOutcome`). *Risk:* ClamAV's signature database is large; the first run downloads it, so the dependency must show
  progress and never block a key handler.
- **YARA rules.** Scan files and folders with user-supplied `.yar` rule sets, via the `yara` program or a managed port.
  Output: file, rule name, matching strings with offsets. Read-only, never executes the sample.
- **Strings extraction.** ASCII and UTF-16LE strings with offsets and a minimum length, as a list that can be filtered. A
  panel (`strings://`-style) or a dialog. Pairs with the hex view: Enter on a string jumps to its offset.
- **Entropy map.** Per-block entropy across a whole file, drawn as a heat strip above the hex view. Shows packed or
  encrypted regions in a file at a glance. The entropy code already exists for executable sections (`ExecutableHeuristics`);
  this applies it to any file.
- **Carving.** Find embedded signatures (ZIP, PDF, PNG, ELF, gzip) inside a blob and extract the bytes from one offset to
  the next into a file. Useful for dropper analysis and for recovering data from damaged files.
- **Full magic-number database.** `FileDisguiseChecker` knows a few signatures; a table of several hundred (from the public
  `file` database) would classify most formats by content.
- **Office macro detection.** Report whether a `.docx`, `.xlsm` or `.doc` file holds a VBA project (`vbaProject.bin`, or
  the OLE `VBA` storage) and list the procedure names that run on open (`AutoOpen`, `Document_Open`) without decompiling or
  running them. The OLE reader (OpenMcdf) is already in the tree.
- **PDF risk indicators.** Scan the object dictionary for `/JavaScript`, `/OpenAction`, `/Launch`, `/EmbeddedFile` and
  `/URI`, and report what is present. The PDF extractor already walks streams; this is a dictionary scan on top.
- **Script obfuscation indicators.** For text scripts (PowerShell, JavaScript, shell): long single lines, base64 blobs,
  `eval`-like calls, char-code arithmetic. Heuristics only, reported as hints, never as a verdict.
- **Mark of the Web.** A file downloaded from the internet, or extracted from an archive that came from one, shows an
  "untrusted" flag in the info strip, read from `Zone.Identifier`, which the streams service already reads. Launching an
  untrusted file asks first.
- **Untrusted-file launch gate.** `FileLauncher` refuses to run a flagged file without a y/n in the footer. This follows the
  destructive-action rule in `CLAUDE.md`.
- **Quarantine folder.** Move a suspect file into a folder whose execute bits are cleared and whose entries open only in
  the viewer, never through the launcher. *Where:* a `quarantine://` URL scheme, if the user wants it browsable.
- **Opt-in sandboxed run.** Launch an untrusted executable inside `bubblewrap` or `firejail` on Linux, or Windows Sandbox
  on Windows, when the user asks for it, with a confirmation that names the sandbox. Default off. *Risk:* a sandbox that
  the user thinks is stronger than it is. The dialog must say what the sandbox does and does not stop.
- **Archive safety, verify and harden.** Check that archive extraction refuses entries whose path leaves the target folder
  (zip-slip, `..` and absolute paths), and add a compression-ratio and total-size ceiling against zip bombs. Confirm first
  what `ArchiveDriveService` and the extract dialog do today; add what is missing.

### 3.3 Network reconnaissance and troubleshooting (networks you run)

- **TLS endpoint inspector.** Connect to `host:port`, show the chain, subject alternative names, protocol version, cipher,
  validity dates and the days left, with warnings for expiry, weak protocols (TLS 1.0 and 1.1) and a name mismatch. Uses
  `SslStream` on a background task; the certificate formatter already draws certificates.
- **Certificate expiry watch.** A background job that checks a saved list of hosts once a day and posts a notification when
  a certificate is within N days of expiry. Jobs poll settings (there is no change notification), per `CLAUDE.md`.
- **HTTP request builder.** Method, URL, headers and body typed in; shows status, headers, timing and body size; keeps a
  history; exports to HAR, which the existing HAR viewer reads. Useful for checking a deployed API's headers, CORS and
  redirects. *Note:* requests carry credentials from the vault, never from the URL.
- **DNS record types and email authentication.** Extend the DNS tool beyond A and PTR to MX, TXT, NS, SOA and CAA, and add an
  email-domain audit that reads the SPF, DMARC and common DKIM selectors and reports what is missing or weak.
- **Banner grab.** On a TCP port the user has confirmed, read the first line the service sends, with a short timeout. Shown
  in the network tools output.
- **Listening sockets with owners.** A list of listening TCP and UDP ports with the owning process, on Linux from `ss -tulpn`
  (or `/proc/net`) and on Windows from the IP helper API. First confirm whether `NetworkManagerDialog` already shows this; if
  it does, this is a check, not a build.
- **Neighbour table with vendor.** The ARP table, with vendor names from the IEEE OUI list (bundled as a small file or
  fetched once as a runtime asset).
- **Packet capture reader.** Read `.pcap` and `.pcapng` files: packet list, conversations, protocol counts, DNS queries and
  TLS server names. A managed parser for the common formats, or `tshark` through the runtime-dependency pattern. Read-only.
- **Live capture.** A capture from a chosen interface through `tshark` or `dumpcap`. Needs elevation, so it stays behind
  the elevation service and a confirmation. Lower priority than reading captures.
- **Scope-checked subnet scan.** The subnet scan exists. Add the scope file (section 5.3), a rate ceiling and a per-run
  summary of what was touched.
- **SSH tunnel manager.** Create, list and stop local, remote and dynamic (`-L`, `-R`, `-D`) port forwards through the SSH
  client the drive already uses, as jobs with a visible state.

### 3.4 Secrets, credentials and identity

- **Secret scanner.** Scan a folder for patterns that look like secrets: private key headers, cloud access key formats,
  personal access token prefixes, and high-entropy strings in assignments. Reports path, line and a masked value (the first
  and last few characters only). Runs as a background task, and the matched value never goes to a log line. *Note:* this is
  the feature developers most often need from a file manager.
- **Password generator with strength estimate.** Random passwords and passphrases from the system CSPRNG, with an entropy
  figure and a reason when a password is weak. Lives in the password manager.
- **Breached-password check (k-anonymity).** Send only the first five characters of the SHA-1 of a password to the Pwned
  Passwords range API and compare the suffixes locally. Opt-in per check, never automatic, and the request is described in
  the dialog before it is sent.
- **TOTP codes.** RFC 6238 one-time codes for vault entries that have a TOTP secret, with a countdown. The secret is stored
  in the vault like any other credential.
- **JWT decoder.** Show the header and claims, flag `exp`, `nbf` and `iat` in human time, and verify a signature only when
  the user supplies the key. Decoding never implies trust.
- **SSH key inspector.** List keys in `~/.ssh` with type, size and fingerprint; flag private keys with permission bits wider
  than `600`; read `authorized_keys` and report option strings such as `from=` and `command=`.
- **Certificate and key tools.** Generate a CSR and a key pair, convert between PEM, DER and PKCS#12, and inspect a private
  key's type. Through OpenSSL as a runtime dependency, or managed where the format is simple.
- **Clipboard auto-clear.** Clear a copied password after a set number of seconds, but only if the clipboard still holds
  what the app put there. Confirm first what the clipboard service does today.
- **Vault auto-lock.** Lock the password manager after an idle period and ask for the master secret again. Jobs poll the
  setting.
- **Credential hygiene report.** List saved profiles that store a password in plain form, or that use a protocol with no
  host pinning, and offer to move them into the vault. Credentials stay out of URLs, as `CLAUDE.md` requires.

### 3.5 Encoding and crypto utilities

These are the tools a security worker reaches for in the middle of an analysis. Most are pure functions, so they are
cheap to test and to put in `iConsole`.

- **Recipe pipeline.** Chain transforms (base64, hex, URL, gzip and zlib, XOR with a key, ROT13, JSON pretty-print, regex
  extract) and apply the chain to the clipboard or a file, with the output updating as each step is added. The transforms are
  pure functions in `iConsole`; the recipe dialog is in `iTerminal.Core`.
- **Hash and HMAC of text.** MD5, SHA family, SHA-3, HMAC with a key; bcrypt and Argon2 verify when given a hash.
- **Single-byte XOR search.** For obfuscated data, try all 256 keys and rank the results by English-text likelihood and by
  chi-square. Analysis of a sample, not an attack on a system.
- **Timestamp converter.** Unix seconds and milliseconds, Windows FILETIME (100 ns since 1601), WebKit and Apple epochs, with
  time zone shown. FILETIME appears constantly in forensic and Windows log work.
- **Number and bit tools.** Hex, decimal, binary, base64 and endianness conversion; bit masks; IPv4 and IPv6 arithmetic
  (subnet and range calculations, which the subnet parser already does for scanning).
- **ASN.1 and DER tree.** Decode any DER blob (certificates, keys, CMS messages) into a tree of tags, lengths and values.
- **Secure random tokens.** Tokens, UUIDs and API-style keys from the CSPRNG, with the alphabet and length chosen by the user.

### 3.6 Hardening and configuration audit (read-only)

Each audit reports findings and remediation text. None of them changes the system. A fix is a command the user runs.

- **Unix permissions audit.** World-writable files and folders, setuid and setgid binaries, files with no owner, and writable
  entries on `PATH`. Runs as a background task over a chosen root.
- **Windows permissions audit.** Built on the access-control service: "Everyone" or "Authenticated Users" with write rights
  on program folders, and services whose binary path is unquoted or writable.
- **Persistence review.** List the places a program can start itself: systemd units and timers, cron, user and system
  autostart `.desktop` files, `~/.profile` and shell rc files, Windows Run and RunOnce keys, scheduled tasks and services.
  Listing only; no removal.
- **Service configuration audit.** Check `sshd_config` (root login, password authentication, empty passwords), `sudoers` and
  the `hosts` file. Rules are written in the app and cite the setting, not a compliance standard.
- **Firewall status.** The active rule set for `ufw`, `firewalld`, `nftables` or Windows Defender Firewall, read-only.
- **Local account review.** Extends the users-and-groups service: accounts with UID 0 besides root, accounts with empty
  passwords, and interactive shells on system accounts.
- **Package vulnerability check.** Installed packages checked against OSV (`osv-scanner`, or the OSV API with only the
  package name and version sent). Opt-in, because it contacts a service.
- **Benchmark-style report.** A dialog that runs a set of the checks above and shows pass, warn or fail for each, with the
  remediation text and the command to run. Exported as Markdown.

### 3.7 Integrity and monitoring

- **File integrity monitor.** Baseline a folder with the hash manifest, recheck it on a schedule, and post a notification for
  each file added, removed or changed. The baseline lives in SQLite (a table of its own), not in `Settings`.
- **Hash-based folder comparison.** `CompareFoldersDialog` compares names, sizes and dates. A "by hash" mode would catch a
  changed file whose size and date were kept.
- **Folder watch.** Watch a folder for new files and post a notification, using a file-system watcher on a background thread
  that hands results to `OnIdle`, never to the control tree.
- **Authentication log summary.** Parse `/var/log/auth.log`, `secure`, `journalctl` output or the Windows Security event log
  into failed-logon counts by user and source address, with a tail mode. Read-only.
- **Windows event log viewer.** Browse the classic logs with a filter on event ID, level and time, for Windows only.
- **Log correlation.** Search one address or user across several log files at once, ordered by time. Pairs with the
  authentication summary.
- **Offline geolocation.** Country and ASN for an address from a local database (MaxMind's licence and file format need a
  check before this is built). No lookup over the network.

### 3.8 Sanitisation and privacy

- **Free-space wipe.** Overwrite the unused space on a volume, extending the secure-wipe service with the same passes and a
  report. Mostly a matter of reusing the wipe code on a different target.
- **Batch metadata strip with a report.** `StripMetadataDialog` works on what is selected. A batch run with a report of what
  was removed from each file.
- **Redact text.** Replace matches of user-chosen patterns (an address, a token, a user name) in a text file, writing a copy
  and leaving the original alone. For sharing a log without its secrets.
- **Session clear.** Clear the command log, folder history, recent searches and the terminal history on demand, with one key
  that asks first. The keys must follow `Documentation/architecture/key-patterns.md`.
- **Auto-lock.** Lock the app after an idle period, with the same unlock as the vault. `ScreenSavers` already draws an idle
  screen; check whether it can take a lock.

### 3.9 Reporting, workflow and integration

- **Findings notes.** Attach notes to a path and its hash; export all of them as Markdown or HTML. The ToDo list is a start;
  a findings type with a hash and a path is what a report needs.
- **Report export.** Any of the dialogs above (checksums, the audit, the secret scan, the TLS inspector) exports its result
  as Markdown or CSV. One shared writer.
- **Security presets in the user menu.** The user menu (Shift+F10) already runs commands. Ship a default menu with entries for
  the checks above, and let users edit it. This is the cheapest way to make the features reachable.
- **Command palette entries.** Every tool above gets a palette entry, so it is reachable without remembering a key.
- **AI bar tools.** Let the AI bar run the read-only checks (hash a file, decode a JWT, scan a folder for secrets). *Risk:*
  content sent to the model is sent to a third party when a cloud model is selected. The secret scanner must not feed a
  matched value to the model, and the approval dialog must show what would be sent.
- **Runtime dependency status.** The runtime-dependencies page lists the external tools in section 4 with the version found,
  or "not installed" with the install action.

### 3.10 Learning and CTF

- **Security workspace preset.** A saved workspace (`Workspaces`) that opens the encoding tools, the disassembler and a
  scratch folder together.
- **Challenge file tools.** Recipes (3.5) and the XOR search pre-loaded in that workspace. This is a configuration, not new
  code, once the tools exist.

## 4. External tools to integrate

Each is an on-demand runtime dependency, detected on the `PATH` or in the app's data folder. None is shipped.

| Tool | Platform | Used for | Section |
| --- | --- | --- | --- |
| ClamAV (`clamscan`) | All | Malware scan behind `IMalwareScanService` | 3.2 |
| YARA | All | Rule-based file triage | 3.2 |
| OpenSSL | All | Certificate and key conversion, CSR generation | 3.4 |
| GnuPG (`gpg`) | All | Signature checks (already used), file encryption interop | 2, 3.4 |
| age | All | Modern file encryption interop | 3.4 |
| minisign | All | Signature checks (already used) | 2 |
| nmap | All | Port and service discovery on owned hosts, optional beside the managed scanner | 3.3 |
| tshark / dumpcap | All | Packet capture reading and live capture | 3.3 |
| testssl.sh | Unix | TLS configuration audit beyond the built-in inspector | 3.3 |
| osv-scanner | All | Package vulnerability check | 3.6 |
| bubblewrap or firejail | Linux | Opt-in sandboxed run | 3.2 |
| Windows Sandbox | Windows | Opt-in sandboxed run | 3.2 |

## 5. Cross-cutting requirements

Every feature above must meet these before it is built:

1. **The 200 ms budget.** Parsing, hashing, scanning and network calls run as async work on a background task, and the
   result is written back through the stale-result check (`IsCurrentUrl()`).
2. **Terminal and rendering rules.** A new loop that reads or writes the real terminal brackets each pass with
   `ConsoleOwnership.TryBeginUse`/`EndUse`.
3. **Tests never touch the machine.** Every new service that scans, resolves names, opens sockets or runs a tool gets a
   sandbox replacement in `SandboxServices`, and the end-to-end suite must never reach the real network.
4. **Secrets stay in the vault.** Credentials come from the vault or the password manager, never from a URL, a log line, a
   model prompt or a `Settings` value.
5. **No `__` keys in `Settings`**, and no schema stamp there either (`CLAUDE.md`).
6. **Logs are redacted.** Serilog calls in `iTerminal.Core` never record matched secret values, file contents or full
   captures.
7. **Destructive or outward actions ask first.** A y/n in the footer for anything that changes a file or contacts a service.
8. **No default network use.** Each outside call is opt-in, described before it is made, and limited to the minimum data.
9. **Honest labels.** A verdict from a heuristic is shown as a hint with its basis, never as a result. The sandbox dialog
   names what the sandbox does not stop.

## 6. Suggested order

The list is ordered by value against effort. Items in the first group reuse code that exists or are pure functions.

**First (cheap, high value, mostly reuse):**

1. Secret scanner (3.4).
2. Recipe pipeline, timestamp converter, JWT decoder, ASN.1 tree and number tools (3.5, 3.4).
3. TLS endpoint inspector and certificate expiry watch (3.3).
4. Unix and Windows permission audits and the persistence review (3.6).
5. ClamAV behind the existing malware-scan interface (3.2).
6. Mark of the Web display and the untrusted-file launch gate (3.2).
7. Archive safety check: zip-slip and zip-bomb limits, verified and completed (3.2).
8. Read-only panel mode (3.1).
9. Security presets in the user menu and palette entries (3.9).

**Second (new parsers or runtime dependencies):**

1. YARA, strings, entropy map and carving (3.2).
2. Office macro and PDF risk indicators (3.2).
3. Evidence bundle and chain-of-custody log (3.1).
4. HTTP request builder and DNS and email audit (3.3).
5. Password generator, k-anonymity breach check, TOTP and SSH key inspector (3.4).
6. File integrity monitor and authentication log summary (3.7).
7. Findings notes and report export (3.9).

**Third (large, or risky without care):**

1. Packet capture reader, then live capture (3.3).
2. Opt-in sandboxed run (3.2).
3. Forensic timeline (3.1).
4. Windows event log viewer and log correlation (3.7).
5. Offline geolocation (3.7).
6. SSH tunnel manager (3.3).
7. AI bar security tools (3.9), after the content-to-model rules are settled.

## 7. Decisions for the maintainer

These choices change what gets built, so they are the maintainer's to make:

- **Scanners as dependencies or built in.** Should ClamAV and YARA follow the Ollama pattern (installed on demand), or is a
  managed YARA port acceptable so the feature works offline with no install?
- **Scope file.** Should active network features (subnet scan, port check, banner grab) require a scope file, or only a
  confirmation each time?
- **Breach check.** Is the k-anonymity request to the Pwned Passwords API acceptable as an opt-in, or should it stay out of
  the app entirely?
- **Sandboxed run.** Is an opt-in sandbox worth the risk of a false sense of safety, or should the app only ever open
  untrusted files in viewers?
- **Priority.** Which of the first-group items should be built first?

## 8. How this list was built

The "Have" column was checked against the source tree and the architecture documents on `main` at the commit this
document was written on. The proposed items were chosen by what a security worker does with files, hosts and secrets, and
then checked against the architecture rules. Tool names and version numbers should be re-checked before an implementation
starts, because the external projects change.
