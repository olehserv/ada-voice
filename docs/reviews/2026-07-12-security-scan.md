# Security Scan — 2026-07-12

> **Status: all 7 findings fixed 2026-07-12** (same day). Fixes were test-driven — a failing test
> first for every behaviour change (findings 1–5), then the minimal fix. Findings 6 (WASAPI device
> race) and 7 (log PII) are not unit-testable in this suite and were fixed by inspection. Full suite
> green after the changes: 104 Core + 98 Audio + 8 Wasapi + 8 Host + 256 App. The findings below stay
> in present tense as of the scan — read them with this banner. No public API removed; two host seams
> gained a read-only member (`ISettingsHost.SettingsWarning`, `ILibraryHost.BrokenVersionIds`).

First security-focused scan of the whole codebase **except** `docs/monetize/` (reviewed
separately). Attacker model for this scan: **malicious or malformed local files**
(`library.json`, `settings.json`, backup/import zips, hand-placed WAVs) and **mistakes by
the app itself** (data loss, a crash on load, PII in logs). Not network attacks — there is
no network. Not local-admin attacks.

Method: two read-only sweeps (logging/OS surface; post-2026-07-04 code) plus targeted reads.
Every finding below was confirmed by reading the flagged code, not from a grep match.
Findings already covered by the [2026-07-04 full review](2026-07-04-full-codebase-review.md)
are not repeated; where a spot overlaps, the new angle is stated.

Highest impact first.

---

## Findings

### [High] 1 — Recording a phrase *version* can write a WAV outside the data root

- **Finding:** A phrase's `Id` is never sanitized. The version WAV name is built straight from
  it, so a crafted `Id` escapes the `audio\` folder when the operator records a version.
- **Where:** `src/AdaVoice.Core/PhraseLibraryService.cs:420`
  (`var fileName = $"{phraseId}-{versionId}.wav";` — no `Path.GetFileName`) →
  `src/AdaVoice.Host/EngineHost.cs:374-376` (`WavFile.Save(AdaVoicePaths.AudioPath(_dataRoot, fileName), …)`) →
  `src/AdaVoice.Core/Storage/AdaVoicePaths.cs:20` (`AudioPath` is a raw `Path.Combine`).
  The 2026-07-11 fix flattens `FileName` in `src/AdaVoice.Core/Storage/LibraryJson.cs:50-58`
  but **not** `Id`.
- **Risk:** An attacker who can write `library.json` (other local software, a tampered folder
  sync, a malicious backup someone restores) sets a phrase `Id` like `..\..\..\evil`. Playback
  stays safe (the `FileName` is flattened). But when the operator records a version for that
  phrase, `WavFile.Save` runs `Directory.CreateDirectory` on the escaped path and writes the WAV
  there — a write **outside** `%LOCALAPPDATA%\AdaVoice`, breaking the "all data stays under the
  data root" boundary (07 §2). It is an arbitrary-*directory* write, not a targeted file
  overwrite: the name always ends in a random `-{versionId}.wav`, and the content is the
  operator's own recording.
- **Fix:** Flatten the composed name in `AddPhraseVersion`:
  `var fileName = Path.GetFileName($"{phraseId}-{versionId}.wav");`. This strips traversal
  without touching `Id` identity or lookup. (Sanitizing `Id` itself in `LibraryJson.Sanitize`
  also works, but changes identity semantics — the composed-name flatten is the smaller fix.)

### [High] 2 — A malformed `library.json` (`"phrases": null`) crashes startup and skips quarantine

- **Finding:** `Sanitize` reads `library.Phrases.Count` and `phrase.Versions` with no null check,
  but `TryParse` catches only `JsonException`. Valid JSON with a null collection throws a
  `NullReferenceException` that escapes and crashes the app before the window opens.
- **Where:** `src/AdaVoice.Core/Storage/LibraryJson.cs:50` (`library.Phrases.Count`) and `:56`
  (`phrase.Versions.Select`); catch at `:35` is `catch (JsonException)` only.
  `Phrases`/`Versions` are settable collection properties
  (`src/AdaVoice.Core/Domain/Library.cs:8`, `src/AdaVoice.Core/Domain/PhraseEntry.cs:23`), and
  System.Text.Json overwrites their `= []` defaults with `null` when the JSON says
  `"phrases": null` (confirmed by a test that reproduced the `NullReferenceException` at
  `Sanitize` before the fix).
  `JsonPhraseRepository.Load:37` does not wrap the `TryParse` call, so the NRE propagates through
  the `EngineHost` constructor.
- **Risk:** This is the doc/code contradiction the scan looked for. `LibraryJson`'s own summary
  and design 04 §3 promise "startup never crashes and never silently starts empty". A single
  malformed field (`"phrases": null`, or one phrase with `"versions": null`) breaks exactly that:
  the app will not start, and the quarantine + backup-recovery path never runs.
- **Fix:** Treat a null collection as invalid input, not as a value to repair — route it through
  the existing quarantine path. Broaden the catch so the NRE is treated like a parse failure:
  `catch (JsonException or NullReferenceException) { return null; }` in `TryParse`. Do **not**
  coalesce `Phrases` to `[]` — that would re-create the silently-empty library the same §3
  forbids.

### [Medium] 3 — Corrupt `settings.json` silently drops the mic calibration, with an audible result

- **Finding:** On any read/parse error, settings load returns a fresh `Settings()`. No notice,
  no quarantine. That discards `micReferenceRms` and `micDuckDb`.
- **Where:** `src/AdaVoice.Core/Storage/JsonSettingsRepository.cs:36-40`
  (`catch (… JsonException or IOException or UnauthorizedAccessException) { return new Settings(); }`).
- **Risk:** Falling back to defaults is deliberate for preferences (07 §3, the class remarks). The
  real problem is narrower: `micReferenceRms` is the wizard calibration reference. If a
  partially-written or corrupt `settings.json` resets it silently, phrases stop being
  loudness-matched and play at the wrong level **into a live call**, and the operator gets no
  hint why — the fix is a full wizard re-run.
- **Fix:** Keep the defaults fallback, but tell the operator when it happened (a startup toast,
  the same way the library load surfaces `Corrupt`/`RecoveredFromBackup`), so a lost calibration
  is visible instead of silent.

### [Medium] 4 — No size cap on the normal load path (`library.json`, `WavFile.Load`)

- **Finding:** Import and backup recovery cap `library.json` at 16 MB, but the everyday load reads
  the whole file with `File.ReadAllText`, and `WavFile.Load` reads a WAV into a doubling
  `List<float>`. Neither has a size guard.
- **Where:** `src/AdaVoice.Core/Storage/JsonPhraseRepository.cs:26` (`json = File.ReadAllText(path)`);
  `src/AdaVoice.Audio/Storage/WavFile.cs:18-24` (`List<float>` + `AddRange`).
  (Distinct from 2026-07-04 M8, which is about *format* validation on load — this is size/OOM only.)
- **Risk:** An oversized or crafted local file causes an out-of-memory crash — at startup for a
  huge `library.json`, or on playback/preview for a huge WAV a user (or bad sync) placed in
  `audio\`. Local denial-of-service, but "restore a folder someone sent me" is a real flow.
- **Fix:** Check the file length before reading it in on the normal load path, using the same
  16 MB cap the archive path already defines (`LibraryArchiveService.MaxLibraryJsonBytes`); for
  WAVs, cap by `new FileInfo(path).Length` before `Load` (the recorder already bounds new takes).

### [Low] 5 — Startup broken-phrase check ignores version WAVs

- **Finding:** Validation flags a phrase whose primary WAV is missing, but never checks the WAVs
  of its versions.
- **Where:** `src/AdaVoice.Core/Storage/LibraryValidator.cs:14-15` (checks `p.FileName` only).
- **Risk:** A phrase with a present primary but a deleted/missing version file is not flagged as
  broken. Impact is contained — playback `File.Exists`-guards and log-skips the missing file
  (`EngineHost.cs:284-288`, `424-426`) — but the operator gets no signal that a version is gone.
- **Fix:** Include version file names in `FindBrokenPhraseIds` (or add a per-version broken flag)
  so the UI can show it.

### [Low] 6 — STOP can throw on the UI thread during a headphone preview

- **Finding:** `StopPreview` reads the render device under a lock but calls `Stop()` outside it,
  and `WasapiRenderDevice.Stop()` has no disposed-guard. `Preview`'s `using` can dispose the
  same device in that window.
- **Where:** `src/AdaVoice.Host/EngineHost.cs:581-587` (`render?.Stop()` after the lock);
  `src/AdaVoice.Host/EngineHost.cs:549` (`using var render`) and `:573` (field nulled in the
  finally); `src/AdaVoice.Audio.Wasapi/WasapiRenderDevice.cs:84` (`Stop() => _output?.Stop();`).
- **Risk:** If the preview finishes at the same moment the operator hits STOP, `StopPreview` can
  call `Stop()` on an already-disposed `WasapiOut` → `ObjectDisposedException` on the UI thread.
  The global handler added in the 2026-07-04 fixes now catches it (so no crash), but it fires on
  the most safety-critical control, and the window is real.
- **Fix:** Guard `WasapiRenderDevice.Stop()`/`Dispose()` with a lock + a disposed flag, and/or
  wrap the `render?.Stop()` in `StopPreview` in a try/catch that ignores `ObjectDisposedException`.

### [Low] 7 — Log file records the Windows username and the monitor device name

- **Finding:** Startup logs the full data-root path (which contains the Windows username) and the
  user-chosen monitor device name.
- **Where:** `src/AdaVoice.Host/EngineHost.cs:93-94` (`… broken at {_dataRoot}`), `:105` / `:111`
  (`MonitorDescription()`), `:538` (`preview → {device.FriendlyName}`).
- **Risk:** The log lives under `%LOCALAPPDATA%` (the user already owns it), so this is low. But
  logs are the thing you send for support, and then the username + device names travel with it.
  **Good news, verified:** phrase titles, category names, and conversation names are **never**
  logged; `AdaVoice.Core`/`AdaVoice.Audio*` have no logging at all.
- **Fix:** Log a relative marker instead of the absolute root (e.g. just "data root OK" or the
  folder name), or accept it and note in the support guide that logs contain the username. No
  change is also defensible at this scale — flagged for visibility.

---

## Checked and clean

These areas were searched and read; nothing to fix beyond what is above or in the prior review.

- **Zip-slip on import / backup restore:** safe. Entry names are never fed to `Path.Combine`;
  extraction destinations use `Path.GetFileName` and are re-keyed to `{id}.wav`
  (`LibraryArchiveService.cs:104,116,241`). Backup restore reads `library.json` into memory only —
  it never writes zip contents to disk (`BackupService.cs:50-75`).
- **Zip resource limits:** enforced — 10 000 entries, 16 MB `library.json`, 256 MB per WAV, 1 GB
  total audio (`LibraryArchiveService.cs:35-38`, checks at `:80-85,231-235`). Closes 2026-07-04 M10.
- **Transactional import:** stage-to-temp then move, rollback on any failure
  (`LibraryArchiveService.cs:145-162`) — the "nothing changed on failure" contract holds.
- **Atomic writes + quarantine:** implemented as design 04 §3 promises — temp + rename everywhere
  (`JsonPhraseRepository.cs:74-85`, `JsonSettingsRepository.cs:48-58`, `WavFile.cs:33-42`,
  `BackupService.cs:30-35`); corrupt `library.json` → quarantine → backup recovery
  (`JsonPhraseRepository.cs:44-67`).
- **FileName flattening:** complete for phrase **and** version `FileName`
  (`LibraryJson.cs:50-58`); the only gap is `Id` (finding 1).
- **`Process.Start`:** 3 sites, all `UseShellExecute`, all app-derived or hardcoded paths
  (backups folder, the VB-CABLE URL, own exe for restart) — none from `settings.json`/`library.json`
  (`EngineHost.cs:509`, `EnvironmentChecksStepView.xaml.cs:62`, `MainWindow.xaml.cs:290`).
- **Registry / env vars / clipboard:** one read-only HKCU theme read
  (`App.xaml.cs:125-127`); no env-var-driven paths in `src`; no clipboard use.
- **Global hotkey:** `WndProc` handles only `WM_HOTKEY` and reads no message payload
  (`Win32HotkeyRegistrar.cs:40-49`).
- **Temp files:** all `*.tmp` / `*.importing` live under the per-user data root and are cleaned up
  on failure — no shared-temp-dir or predictable-name-in-world-writable risk.
- **Conversations:** persisted in `library.json`; dangling phrase ids are pruned at load
  (`PhraseLibraryService.cs:93-109`); no file path is ever built from conversation data. Missing
  id de-dup / count caps is a correctness gap, not a security or data-loss one.
- **Serilog retention:** bounded by the sink defaults (31 daily files). Worth making the limits
  explicit (`fileSizeLimitBytes`, `retainedFileCountLimit` at `App.xaml.cs:46-48` /
  `Program.cs:14-17`), but not a vulnerability.

---

## What to learn from this

- **A choke-point fix is only as complete as the fields it covers.** The 2026-07-11 flatten fix
  did the right thing — one place, `LibraryJson`, for load/backup/import — but it flattened
  `FileName` and missed that `Id` also becomes a file name (for versions). When you add a
  sanitize choke point, list *every* field that reaches a file API, not just the obvious one.
- **A "never crashes" promise needs a catch wide enough to keep it.** Finding 2 is a narrow catch
  (`JsonException` only) sitting under a broad promise ("startup never crashes"). Match the catch
  to the promise, and let malformed input flow to the recovery path you already built.
