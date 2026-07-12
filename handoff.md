# AdaVoice — Handoff & Progress

**Live status of the project.** Read this first when you (or a new session) pick the work
back up. It answers one question: *where are we right now?*

- **What it is:** done work, work in progress, next steps, and open questions.
- **What it is not:** the strategy (see [roadmap](docs/roadmaps/mvp-roadmap.md)) or the
  decision record (canonical table in
  [design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).
- Details of past work live in git history and in the dated docs under `docs/reviews/`.
  This file stays short on purpose.

_Last updated: 2026-07-12._

## Status in one line

**The app is built and verified on the target machine** — engine, recorder, library, Board UI,
setup wizard, Settings window, stop hotkey, backups, export/import, **Conversations** (ordered
phrase scripts with a step-by-step highlight), **phrase versions** (alternate takes, randomized
during a Conversation step); 465 tests passed + 16 skipped across 6 projects (104 Core + 98 Audio +
8 Wasapi + 8 Host + 241 App + 6 Server). Monetization: Phase 0 (server scaffold) shipped, no
domain code yet.

## Latest work (2026-07-12)

- **Security scan + fixes (whole codebase, monetize excluded).** A read-only scan
  ([reviews/2026-07-12-security-scan.md](docs/reviews/2026-07-12-security-scan.md)) found 7 issues;
  all fixed the same day, test-driven where testable. Highlights: **(1, High)** the 2026-07-11
  path-traversal fix flattened `FileName` but not the phrase `Id` that version WAV names are built
  from — recording a version for a crafted `library.json` phrase could write outside `audio\`; now
  the composed name is flattened in `PhraseLibraryService.AddPhraseVersion`. **(2, High)** a malformed
  `library.json` (`"phrases": null`) crashed startup and skipped quarantine (`LibraryJson.Sanitize`
  NRE, only `JsonException` caught) — now null collections are normalized/quarantined and the app
  starts. **(3)** a corrupt `settings.json` silently reset the mic calibration with no notice — now a
  startup toast (`ISettingsHost.SettingsWarning`). **(4)** size caps on the normal `library.json` and
  `WavFile.Load` reads (OOM guard). **(5)** the Versions window now flags a version whose WAV is
  missing (`ILibraryHost.BrokenVersionIds`) without marking the whole phrase broken. **(6)**
  `WasapiRenderDevice.Stop/Dispose` now lock + disposed-guard (STOP-during-preview race). **(7)** the
  Windows username no longer logged. Two seams gained a read-only member; no public API removed. Full
  suite green (104 Core + 98 Audio + 8 Wasapi + 8 Host + 256 App). Relevant to monetization: findings
  1–2 live in the Core storage code phases 0–6 extend, so worth having closed first.
- **UX modernization workstream started** — audit
  ([ux-layout-style-audit.md](docs/design/audits/ux-layout-style-audit.md)) confirmed most of
  the owner's known-issue list was already fixed by the 2026-07-11 redesign, and found the real
  remaining gaps: `SettingsWindow`'s "Done" hidden below scroll (B1), `MessageBox` breaking the
  Fluent look everywhere else (E2), destructive buttons not using `Danger` (D1). Plan:
  [ux-structural-fix-plan.md](docs/design/plans/ux-structural-fix-plan.md). New rulebook:
  [wpf-ux-design-rules.md](docs/design/wpf-ux-design-rules.md). **Pass 2 (B1) shipped** —
  `SettingsWindow`'s Done button moved to a fixed footer row; 238/238 App tests green. Built via
  subagent-driven-development: one fix round for a stray full-file XAML reformat the
  implementer's `xstyler` run introduced, and a second fix round for a margin regression
  (Done ended up flush against the window edges after losing its parent's ambient margin) that
  the automated reviewer missed but a screenshot caught. **Pass 4 (D1/D2) shipped** — Danger
  appearance on 4 destructive buttons (category/conversation Delete, phrase Remove, version ✕)
  across 3 dialogs, `IsDefault` added to Recorder's Save; clean on first review, visually
  confirmed. **Owner UX rework batch shipped** (ad-hoc feedback after reviewing screenshots, not
  part of the original audit): `SettingsWindow`/`ManageCategoriesDialog`/
  `ManageConversationsDialog` Done buttons now green (`Success`); row heights matched across
  text/dropdown/button controls; category Delete and conversation-member Remove are now
  icon-only red "✕" (conversation-level Delete deliberately left as text); category color
  dropdown shows swatch only, no hex; per-row Save buttons removed in both manage dialogs —
  name/color edits now auto-persist on blur/selection-change via a new code-behind pattern
  (`RowField_Committed`, mirrors `SettingsWindow`'s existing slider-commit pattern), no
  ViewModel changes; new-conversation "Add" now sits on the same line as its input and is a
  green checkmark. 238/238 App tests green throughout. **Pass 3 (C1, MainWindow resize check)
  verified, no fix needed** — rather than a manual live-app resize, added a permanent
  regression screenshot (`MainWindow_board_wide`, 1366×780, 10 phrases) proving the search/
  filter row and phrase `WrapPanel` hold up at desktop width: no wrap/clip/overlap, STOP stays
  full-width and readable. 238/238 App tests still green. **New owner UX concern captured as
  Pass 6 (docs only, no code yet):** phrase tiles on the board have inconsistent height
  depending on tag count (and the pre-existing F1 finding about long-title height was folded
  in); plan is a fixed tile `Width`/`Height`, a title clamp, and a capped tag list with a "+N"
  overflow chip (full tags stay visible via the tile's existing "Edit…" menu item) — see `Pass 6`
  in [ux-structural-fix-plan.md](docs/design/plans/ux-structural-fix-plan.md), promoted ahead of
  Pass 2b. Both Pass 6 and Pass 2b (`MessageBox`→`ContentDialog`) await approval to start.
- **Monetization Phase 0 (repo scaffold) shipped, docs only otherwise.** Five empty-but-buildable
  `server/` projects (Api → Infrastructure → Domain, Workers → Infrastructure) wired into
  `AdaVoice.slnx`, with a dependency-direction guard test; dev-only Postgres 16
  `docker-compose.yml` (loopback-bound). Hosting/API-URL (OQ-01/OQ-02) deferred to Phase 11,
  owner Oleh, 2026-07-12 — see [open-questions §9](docs/monetize/open-questions.md#9-resolved).
  Plan: [2026-07-12-monetize-phase-0.md](docs/superpowers/plans/2026-07-12-monetize-phase-0.md).
  Next: Phase 1 (domain model + database).

## Latest work (2026-07-11)

- **UI redesign — "Studio Graphite" + light/dark theming (working tree, pending commit).**
  Owner disliked the old look; picked a new direction (3 shown) and asked to add light + system
  theme. Shipped: neutral phrase tiles with a slim category **edge marker** (was full-colour
  fill); refined cool-graphite palette; **light + dark following the OS** at runtime
  (`ApplicationThemeManager.ApplySystemTheme` + `SystemThemeWatcher`), colour tokens split into
  `Theme/Tokens.Dark.xaml` / `Tokens.Light.xaml` (swapped on `ApplicationThemeManager.Changed`),
  all views moved to `DynamicResource` for theme brushes. Accent now derived from the `Accent`
  token (one source, no code/XAML duplication). Fixed the raw-chrome Delete button (missing
  `BasedOn`) and `PhraseButtonStyle` `BasedOn`. Contrast ≥ 4.5:1 verified in both themes; 238 App
  tests green; screenshots (both themes) render via `ADAVOICE_SCREENSHOTS=1`
  (`+ ADAVOICE_SCREENSHOT_THEME=Light`) into `docs/ui/screenshots/after` and `after-light`.
  Design docs 05/09/10 updated. UI-only — no bindings/ViewModels changed.

- **Security fix: path traversal via `library.json`.** A security review (security-review skill +
  Semgrep Guardian) found three confirmed issues sharing one root cause: phrase/version `FileName`
  fields were read verbatim from `library.json` and passed straight into file APIs via
  `AdaVoicePaths.AudioPath` (a raw `Path.Combine`, so an absolute or `..\` name escapes `audio\`).
  An attacker able to write `library.json` (other local software, a tampered folder sync, a
  malicious backup restore) could (1) exfiltrate an arbitrary file into an export zip the operator
  then shares, (2) move/relocate an arbitrary file when a phrase is deleted, (3) read an arbitrary
  file into playback. The library **import** path already flattened the same field with
  `Path.GetFileName` — this was an inconsistent trust boundary, not a missing concept. Fix: one
  choke point — `LibraryJson.TryParse` (the single JSON→`Library` path for load, backup recovery,
  and import) now flattens every `FileName` to a bare name, so all downstream sinks inherit the
  guarantee. Well-formed libraries are unchanged. 2 new tests (unit-level flatten + end-to-end
  "export doesn't leak an outside file"). 97 Core tests green; full solution builds clean.

## Latest work (2026-07-08)

- **Fixed: "Add version" closed the Versions window, and a second take in the same session silently
  went to the wrong place.** Two bugs found via user testing of the phrase-versions feature above.
  (1) Clicking "Add version (record)…" used to close the Versions window before opening the
  Recorder, so seeing the new take meant reopening Versions by hand. Fix: the Recorder now opens
  on top of the still-open Versions window (`PhraseVersionsViewModel.RecordVersionCommand` calls
  back into `BoardViewModel.RecordVersionForPhrase`, replacing the old close-then-read-a-flag
  handoff); Versions refreshes its own tile grid once the Recorder closes. (2) Recording, saving,
  then recording *again* without closing the Recorder silently created a brand-new unrelated phrase
  instead of a second version — `SaveTakeAsVersion` cleared the "which phrase is this a version of"
  stash right after the first save, so a second save in the same session found no stash and fell
  through to the normal "new phrase" path. Fix: the stash now persists across multiple takes in one
  Recorder session, and is cleared exactly once — in `BoardViewModel.EndVersionRecordingSession()`,
  called from `RecorderDialog.OnClosing` — so it can't leak into a later, unrelated recording either.
  4 new regression tests. User-verified on the real app (both fixes).
- **Fixed: no way to stop a headphone preview.** "Test on headphones" and the Versions window's
  ▶ both used `EngineHost.Preview` — a blocking call with no stop hook, so the big STOP button
  did nothing to it, and closing the Versions window left it playing. Root cause: `Preview` built
  its own `WasapiRenderDevice` without keeping a reference anywhere reachable. Fix: `EngineHost`
  now tracks the in-flight preview device and exposes `StopPreview()` (new on `IPlaybackHost`);
  `StopPhrase()` — the one action behind the STOP button and the hotkey — calls it too, so a
  single control silences either the call or a preview. Board: `IsPreviewPlaying` (separate from
  `IsPhrasePlaying`, since both can be true at once) drives a new `CanStop` that the STOP button
  binds to. Versions window: each tile's ▶ toggles to ■ while it plays (needed
  `AllowConcurrentExecutions` on the Play command — its default disables the button for the whole
  call, which would have made the ■ unclickable); closing the window (any way) now stops playback
  via the dialog's `Closed` handler. 10 new tests. **Needs a hardware smoke test** — the actual
  stop path (`WasapiOut.Stop()` → the render device reporting Stopped) can't be exercised by the
  fakes; confirm on the real app that STOP/■/Close all actually cut the audio.
  Follow-up fix: the ▶→■ toggle never showed — `Content="▶"`/`ToolTip="Play"` were set as local
  XAML values on the button, which always outrank a `Style.Triggers` `Setter` in WPF's property
  precedence, so the `IsPlaying` trigger could never win. Fixed by moving both into the `Style` as
  plain `Setter`s (same tier as the trigger, where triggers correctly take priority). No test
  added — this is a pure XAML-rendering bug and the repo has no view-rendering test harness (only
  view-model tests), and adding one for this alone isn't worth it (project style, "good enough for
  now"); it's part of the same hardware smoke test above.

## Latest work (2026-07-07)

- **Phrase versions shipped:** a phrase keeps its primary recording and can gain extra alternate
  takes ("versions"), managed from a dedicated **Versions window** (`PhraseVersionsDialog`) — a
  board-like tile grid (primary tile first, then one tile per version), each playable, versions
  renamable inline and deletable, plus "Add version (record)…". Opened via the phrase tile's
  "Versions…" context-menu item (its own command, separate from Edit). A new per-conversation
  "Play a random version" checkbox (`ManageConversationsDialog`) makes each phrase play a uniformly
  random pick from primary + all versions while stepping through that conversation; a plain board
  click always plays the primary, unaffected. Data model is additive (`PhraseEntry.Versions`,
  `Conversation.UseRandomVersion`) — no `Library.Version` bump. Library export/import intentionally
  drops version audio for v1 (logged when it happens). Plan (combined with design, went through Plan
  Mode instead of the brainstorming skill, revised 2026-07-08 when versions moved out of the Edit
  dialog into their own window): [phrase-versions plan](docs/superpowers/plans/2026-07-07-phrase-versions.md).
  **Needs a user smoke test** — record a phrase, add two versions, build a conversation with the
  random flag on, and confirm playback actually varies (not exercised here: no audio hardware in
  this environment).
- **Conversations shipped:** a new entity — an ordered, named group of existing phrases for a
  call script. `ManageConversationsDialog` (add/rename/delete a conversation, add/remove/reorder
  its phrases). The Board's Conversation filter shows only that script's phrases, in order, and
  highlights the next expected phrase as the operator plays through it (jumps to whatever was
  actually played + 1, so it tracks a caller who skips around). Deleting a phrase quietly prunes
  it from every conversation. Design: [conversations spec](docs/superpowers/specs/2026-07-06-conversations-design.md).
  Plan: [conversations plan](docs/superpowers/plans/2026-07-06-conversations.md).
- **Filter controls redesigned:** the Category filter (pre-existing) became real multi-select
  (checkboxes) — pick several categories, board shows the union. Both Category and Conversation
  filters moved from ComboBox+button pairs to two compact menu buttons ("Categories…"/
  "Conversations…"), each opening a native menu with "Manage…" at the top. This was a necessary
  follow-up: the original 2-ComboBox filter row (built for Conversations) was proven by real WPF
  layout measurement to overflow the window's width even at default size — simple width tuning
  couldn't close the gap. Design: [filter-controls spec](docs/superpowers/specs/2026-07-07-filter-controls-redesign.md).
  Two bugs caught mid-flight and fixed: a step-pointer NRE from a re-entrancy ordering issue, and
  re-selecting the same conversation being a silent no-op (both have regression tests). Merged to
  `main`; user visually confirmed the redesigned filter row on the real app.

## Previous work (2026-07-06)

- **Recorder moved to a modal window** (owner request): the Board's bottom record strip is
  gone; a Record button sits in the filter row (and the empty-state cards still work). All
  entry points go through `BoardViewModel`'s new `showRecorder` callback, so the window opens
  even when the start fails (it shows why). Closing mid-take stops the recorder and keeps the
  take pending; Record with a take already recording/waiting **reopens the recorder instead of
  starting (and overwriting)** — the Board's Record button lights amber while a take waits.
  Fixed-height dialog on purpose: `SizeToContent` mis-measures under FluentWindow chrome and
  would jump between recorder states.
- **Notices became severity-colored toasts** (owner request): the inline notice text under the
  engine buttons is gone; all `Notice` messages now pop bottom-right of the board area (never
  over STOP) via `BoardViewModel.Notified` — neutral info, amber warning, red error. The
  library-load warning shows as a longer toast on startup. `Notice` stays as VM state (tests).
  364 tests green (4 new this session); warning toast verified by screenshot.
- **UI redesign shipped** (brief: [design 10](docs/design/10-ui-redesign-brief.md), tokens:
  [design 09](docs/design/09-design-system.md)). Expanded token system (surface ramp, subtle
  borders, status tints, hover/press overlays, radius scale), engine-state **status pill**,
  tile hover/press feedback, brand accent applied in code (Primary buttons used to take the OS
  accent color), all six windows on dark `FluentWindow` chrome, `Esc` = stop and `Ctrl+F` =
  search, readable tag chips (colored border, primary text). The OFF AIR banner was **removed**
  (owner call): the OFF AIR toggle now lights amber (`Caution`) while off air, with the amber
  status pill as the second indicator. 360 tests green; Board, Settings, and tile hover verified
  by screenshot. **Needs a user smoke test**: wizard + dialogs, the lit OFF AIR toggle, playing
  ring with a live engine. The Conversations plan's Task 7 carries a compatibility note (its
  XAML snippets predate the redesign).
- **Slice 2 smoke test — PASSED.** User ran the full checklist (category-empty CTA, search
  Clear, repair dialog, Processing state, blank-title guard, wizard spinner): "works great, no
  bugs detected." Slice 2 is now fully done, not just shipped.

## Previous work (2026-07-05)

- **Docs consolidation:** deleted the stale `.ukr/` mirror, executed plan/spec checklists
  (`docs/superpowers/`), the two 2026-07-04 fix plans, and the frozen implementation plan.
  Git history keeps them all. Living docs were updated to match the real project state.
- **Monetization design:** full B2B licensing/billing documentation in
  [`docs/monetize/`](docs/monetize/README.md) (start at its README) plus 6 ADRs in
  [`docs/adr/`](docs/adr/). Key decisions: ASP.NET Core backend in a new `server/` folder,
  PostgreSQL + EF Core, ES256-signed 24-hour license tickets with offline grace (7 days paid /
  2 days trial), DPAPI client storage, refresh-token rotation, manual invoice billing v1,
  payment-provider webhooks v2. Next: answer OQ-12/OC-06 (device vs per-seat limits) in
  [open-questions](docs/monetize/open-questions.md), then Phase 0 of the
  [monetize roadmap](docs/monetize/implementation-roadmap.md). _(Superseded 2026-07-12: Phase 0 shipped; OQ-12/OC-06 gates Phase 4, not Phase 0 — see "Next action".)_
- **Slice 2 (interaction-state gaps) shipped:** repair dialog for broken phrases,
  category-empty CTA, search Clear + query echo, Recorder Processing state + hardened
  `SaveTake` (closes review finding M15), wizard per-check spinner. Fully reviewed;
  360 tests green; pushed to `main`; smoke-tested (see 2026-07-06 above).

## Done (compact history, newest first)

- ✅ **Conversations + filter-controls redesign** (2026-07-07): see "Latest work" above.
- ✅ **Recorder modal, toast notices, UI redesign** (2026-07-06): see "Previous work" above.
- ✅ **Slice 2 — interaction-state gaps** (2026-07-05): see "Previous work" below.
- ✅ **"Next touch" review fixes** (2026-07-04): engine recovery M4–M7, recording/calibration
  safety M1/M2, transactional import + zip caps M9/M10, WASAPI COM hygiene M13, and two new
  test projects (`Host.Tests`, `Audio.Wasapi.Tests`) with an injectable `EngineHost` (H11).
- ✅ **Top-10 risk fixes** (2026-07-04): all Critical/High findings from the
  [full codebase review](docs/reviews/2026-07-04-full-codebase-review.md) fixed — mic-duck
  relay across rebuilds (C1), read-error write guard (C2), rebuild backoff (H1), global
  exception handlers + crash restart + single-instance mutex (H2/H3/H4), async preview
  (H5/H6), start-error surfaced in the status bar (H7), COM `[PreserveSig]` (H8), import
  re-keys WAVs (H9), drift off the audio threads (H10). Committed and smoke-tested.
- ✅ **Settings window** (2026-07-02/04): Levels (duck slider, re-run calibration), Behavior
  (always-on-top, retrigger toggle, hotkey status), Language & Backup (language picker,
  export/import, backup info). Smoke-tested by the user (a crash-on-open bug was caught and
  fixed). Deferred to a future slice: Devices group (needs live audio metering) and true
  hotkey reassignment.
- ✅ **Board library UI** (2026-07-01/02): edit/delete/search/category filter, category
  manager, colour-filled tiles with WCAG auto-contrast, coloured reusable tag chips, window
  placement memory, test-on-headphones. Smoke-tested by the user.
- ✅ **Setup wizard** (2026-07-02): environment checks (+ VB-CABLE download link), voice
  calibration (+ countdown ring), hotkey status, instructions, first-call card. First-run
  trigger + re-run via **Setup…**. Smoke-tested by the user. v2 follow-up (not started):
  device pickers, live meters, loopback self-test.
- ✅ **Hardware run of the full loop — PASSED** (2026-07-01): engine + recorder + storage +
  preview verified end-to-end on the target machine, including cable unplug/replug recovery.
- ✅ **Global stop hotkey** (2026-07-01): `Pause` (fallback `Ctrl+F12`) via `RegisterHotKey`;
  stops the phrase only, works while Chrome is focused.
- ✅ **Duck slider + WPF-UI Fluent theme** (2026-07-01): live mic-duck slider; dark Fluent
  chrome, design-09 tokens, save toast; Topmost preserved.
- ✅ **Phase 2 storage** (2026-06): categories/tags, delete-as-orphan, daily zip backups +
  recovery, export/import, `settings.json`, corrupt-library quarantine, atomic writes.
- ✅ **Phase 1 audio core** (2026-06): `AudioEngine` state machine (Stopped/Live/OffAir/
  Degraded, rebuild + backoff, watchdog), WASAPI factory + device monitor, `EngineHost`,
  Recorder (trim + loudness match), storage + preview vertical.
- ✅ **Operator pilot — PASSED** (2026-06-29): supervised real-call pilot; user: "tested
  everything, works awesome". Script kept for re-runs:
  [operator-pilot.md](docs/plans/operator-pilot.md).
- ✅ **Phase 0 spike gate — PASSED** (2026-06-15): Architecture A (VB-CABLE + in-app mixer)
  confirmed against a real Zoho call. Results: [spike/PHASE0-RESULTS.md](spike/PHASE0-RESULTS.md)
  (file exists; exact measured latency/AGC numbers were never filled in — still TBD there).
- ✅ **Design phase** (2026-06-10): 9 design docs, eng + design reviews cleared,
  24 canonical decisions locked.

## In progress

**Conversations smoke test (user):** the filter-row redesign was visually confirmed on the real
app (fits, opens, filters correctly), but the full Conversations flow hasn't had an end-to-end
pass yet: create a conversation with several phrases in `ManageConversationsDialog…`, reorder
them, select it from the Board's Conversations button, play through in order and out of order,
confirm the step highlight follows correctly, delete a phrase that's in an active conversation.

## Next action

**UI/UX pass + localization** — remaining slices (scope + rationale in
[ui-ux-localization-scope.md](docs/plans/ui-ux-localization-scope.md)):

1. ✅ Settings window — done, smoke-tested.
2. ✅ Interaction-state gaps — done, smoke-tested.
3. **Full/Docked responsive layout** — not started; needs a design decision first (bring back
   the category rail at ≥720 px, or keep dropdown-only and update design 05).
4. **Localization retrofit (UA/PL/EN)** — last, after slice 3's strings exist. All UI strings
   so far are English-only; a `.resx` retrofit is known debt.

Separately, **monetization**: Phase 0 (repo scaffold) is done. Phase 1 (domain model + database)
is next and is **not** blocked. OQ-12/OC-06 (device vs per-seat limits) must be answered before
Phase 4 (device activation), not before Phase 0/1.

**Monetization** — next step is Phase 1 of the
[monetize roadmap](docs/monetize/implementation-roadmap.md) (domain model + database).

## Open follow-ups (named so they're not lost)

- **Configurable monitor device:** preview uses the default output as a stand-in; real
  selection comes with the Settings Devices group.
- **Recorder live level meter + no-signal detection:** deferred until live audio-capture
  polling exists; bundle with the Devices group.
- **2nd-capture fallback:** if a driver refuses a second WASAPI capture client, fall back to
  tapping the engine's capture. Watch for it on hardware.
- **Cold-start auto-retry into Degraded:** a failed `Start` currently stays Stopped with the
  error surfaced.
- **Fill in `spike/PHASE0-RESULTS.md` measured numbers** (latency, AGC notes) if they are
  ever re-measured; the gate itself passed.
- ✅ **Screenshot harness `after-light/` dark-theme bug — fixed (2026-07-12, commit `adda0a7`).**
  Root cause: closing a WPF-UI `FluentWindow` resets `ApplicationThemeManager` back to the OS
  theme as a side effect, so the fixture's one-time theme apply at startup only held for the
  first window. Fix: `ScreenshotHarness` now re-applies `WpfAppFixture.Theme` before building
  every window. Verified — `after-light/settings.png` now renders correctly light.
- Post-MVP backlog lives in the [roadmap](docs/roadmaps/mvp-roadmap.md#deferred-post-mvp-backlog).
