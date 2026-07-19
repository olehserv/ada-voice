# AdaVoice — Handoff & Progress

**Live status of the project.** Read this first when you (or a new session) pick the work
back up. It answers one question: *where are we right now?*

- **What it is:** done work, work in progress, next steps, and open questions.
- **What it is not:** the product strategy or the decision record (canonical table in
  [design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).
- Details of past work live in git history and in the dated docs under `docs/reviews/`.
  This file stays short on purpose.

_Last updated: 2026-07-19._

## Status in one line

**The app is built and verified on the target machine** — engine, recorder, library, Board UI,
setup wizard, Settings window, stop hotkey, backups, export/import, **Conversations** (ordered
phrase scripts with a step-by-step highlight), **phrase versions** (alternate takes, randomized
during a Conversation step); 508 tests passed + 16 skipped across 6 projects (107 Core + 98 Audio +
8 Wasapi + 12 Host + 253 App + 30 Server DB-less) plus 30 server integration tests against
PostgreSQL 16. Monetization: Phases 0–2 shipped — the full 13-table EF Core schema, multi-tenant
query filters and idempotent seeder (Phase 1), and the **auth API** (Phase 2): ES256-JWT
login/refresh/logout/change-password/me, rotating refresh tokens with family-revocation reuse
detection, account lockout, per-IP rate limiting, RFC 7807 errors (typed `ProblemDetails`), and
**batched audit logging** (see 2026-07-19 below — audit writes are no longer synchronous with
the request).

## Latest work (2026-07-19)

- **Pass 2b — scoped to the board's delete-confirm dialog: `MessageBox` → `ui:ContentDialog`.**
  Exploration found the plan's "inject one service into `MainWindow`" assumption broke on a real
  structural fact: 4 of the audit's 5 `MessageBox` prompts are raised from `BackupSettingsViewModel`,
  which lives inside the *modal* `SettingsWindow` (`ShowDialog()`, `Owner = MainWindow`) — a
  `ContentDialog` hosted in `MainWindow` would render behind that modal child and be unreachable.
  Owner chose the low-risk cut: migrate only `ConfirmDelete` (the board's own prompt, the only one
  raised from the non-modal window) now; leave the 4 SettingsWindow prompts and the 2 `App.xaml.cs`
  system prompts (`MessageBox`, by design) for Phase C, which reworks those dialogs anyway. Added a
  `ui:ContentDialogHost` overlay to `MainWindow.xaml`, a `ContentDialogService` wired in the
  constructor, and made `ConfirmDelete` `async Task<bool>` via `ShowSimpleDialogAsync` (CTRL-008:
  Primary button says "Delete", not "Yes"). That forced `BoardViewModel`'s `_confirmDelete` delegate
  and `Delete` command async (`IAsyncRelayCommand`) — the ripple the plan warned about, but scoped
  to one command. Verified two ways: build + full test suite green (271 App tests, 2 Delete tests
  updated to `await ExecuteAsync`), and a live smoke test against the running app (FlaUI driver,
  since the dialog isn't a standalone `Window` and the existing screenshot harness can't render it)
  confirmed the dialog is a true in-window overlay (top-level window count stayed at 1, unlike the
  old `MessageBox`), Escape closes it without deleting the phrase, and the Delete button removes it
  end-to-end. One thing to watch, not yet conclusively verified: whether Escape *also* silently
  fires `MainWindow`'s window-level `Escape → StopCommand` panic-stop binding (a `MessageBox` was a
  separate top-level window so its Escape never reached that binding; a same-window `ContentDialog`
  overlay might bubble unhandled key events up to it) — harmless today since `StopCommand` no-ops
  when nothing is playing, but worth a closer look if Phase C adds a dialog that opens *during*
  playback. See [ux-structural-fix-plan.md](docs/design/plans/ux-structural-fix-plan.md) (Pass 2b)
  for the full writeup.
- **Server Phase-2 review comments resolved: typed `ProblemDetails`, an `AuditEntry` DTO, and
  batched audit persistence.** Three follow-ups on the just-shipped auth API. **(1)**
  `GlobalExceptionHandler`/`AuthRateLimit` now build a real `ProblemDetails` instead of an
  anonymous object (`code`/`correlationId` ride in `Extensions`, still serializing at the JSON
  root — both existing tests needed no changes). **(2)** `IAuditWriter.WriteAsync`'s 8 positional
  parameters (where `userId` fed both `EntityId` and `ActorUserId`) became one `AuditEntry`
  record with `required` init-only properties, closing the transposition risk. **(3)** Audit rows
  are now enqueued onto a bounded `Channel` and batch-persisted every 10 s (configurable,
  `Audit:*`) by a new `AuditFlushService` background service, instead of one `SaveChangesAsync`
  per request. Two correctness points that made this safe rather than just faster:
  `AuditableTenantInterceptor` now stamps `CreatedAt` only when unset, since the writer captures
  the real event time at enqueue (not at the later flush) — a naive change would have misdated
  every batched row; and the enqueue always uses `CancellationToken.None`, not the request's
  token, so a client disconnecting right after a failed login or lockout can no longer drop its
  own security-audit row. Trade-off accepted knowingly: audit rows are eventually consistent
  (up to one flush interval) and a queue full during a DB outage makes new requests stall on
  enqueue rather than fail fast. Three existing integration tests that asserted audit rows
  immediately after the HTTP call now poll instead (`AuditPolling` helper); two new focused tests
  cover the batching mechanics directly (enqueue-time `CreatedAt` survives a real flush delay;
  `StopAsync`'s shutdown drain — not the periodic tick — persists rows written just before a
  graceful stop). 60 server tests green (30 DB-less + 30 integration, was 28).
- **Pass 6 (phrase tiles) confirmed shipped, then found + fixed a real layout bug in it.**
  Re-verifying [ux-structural-fix-plan.md](docs/design/plans/ux-structural-fix-plan.md)'s Pass 6
  (its status table still said "not started" — stale; the work had actually landed in Phase B),
  the owner spotted inconsistent tile heights directly in a fresh render. Confirmed via live
  visual-tree measurement (not just the screenshot): the tile's `ui:Button` hit-box was correctly
  fixed at 128 px, but WPF-UI's `ui:Button` control template centers its `ContentPresenter`
  instead of stretching it, so `VerticalContentAlignment="Stretch"` on `PhraseButtonStyle` did
  nothing — the visible tile still sized itself to tag count/title length (a no-tag tile's
  content measured ~89.6 px vs. ~108.8 px for a 3-tag/long-title one), silently reintroducing the
  exact defect Pass 6 was meant to fix. Fixed with one explicit `Width`/`Height` on the tile's
  content-root `Grid` (`MainWindow.xaml`) matching `PhraseButtonStyle` — validated live before
  and after the change, plus a new permanent regression test
  (`PhraseTileLayoutTests`, confirmed red without the fix, green with it). 271 App tests green
  (248 passed + 23 screenshot, skipped without `ADAVOICE_SCREENSHOTS=1`; was 270/248+22).

## Latest work (2026-07-18)

- **"Pine Signal" brand redesign — Phase A (foundation) shipped.** A green+red brand
  restyle was designed in three phases: **(1)** a UX audit (screenshots both themes +
  wpf-code-auditor + wpf-architect agents) produced 10 findings — top ones: light-theme
  wizard check colors fail WCAG AA (frozen dark-hex brushes in `Converters.cs`),
  no confirm on version/conversation/category deletes, Recorder's destructive Discard
  in the rightmost commit position, the unthemed conversation `ListBox`, and engine state
  readable only as an 8 px dot. Architecture review: token layer is restyle-ready; the
  leaks are WPF-UI `Danger`/`Success`/`Caution` appearances (~15 sites) and the two
  converter brushes. **(2)** Three AA-verified HTML mockups in
  [docs/design/mockups/](docs/design/mockups/README.md). **(3)** Owner picked a mix:
  **variant 3 "Scarlet Pine" base + variant 2's gradient window & glows + a state-lit
  window** (background gradient follows engine state: green LIVE / amber OFF AIR /
  red DEGRADED / grey STOPPED; green stays "live", red stays reserved for
  hot/destructive). [09-design-system.md](docs/design/09-design-system.md) specifies
  the full target; implementation is planned in 4 review-gated phases in
  [brand-redesign-implementation-plan.md](docs/design/plans/brand-redesign-implementation-plan.md).
  **Phase A (tokens + key ownership) shipped**: new Pine Signal palette in
  `Tokens.Dark/Light.xaml` (incl. unwired `Surface.Window.*` state gradients for Phase B);
  `CheckStatusToBrushConverter`'s two frozen hex brushes replaced with `DynamicResource`
  style triggers, `HexToBrushConverter`'s stale `#2B2B2B` fallback now resolves
  `Surface.Raised` live; motion tokens (`Motion.Fast/Base/State`, easings) + chunkier
  radii (`Radius.Control` 10, `Radius.Panel` 14) added to `Tokens.xaml`; MainWindow's
  five inline styles extracted into `Controls.xaml`. **One item descoped with a real
  finding**: a `Theme/WpfUi.Overrides.xaml` meant to re-point WPF-UI's `Danger`/`Success`/
  `Caution` semantic keys at the brand was built, wired in, and *looked* right on an
  eyeballed screenshot — but pixel-sampling the PNG and `DependencyPropertyHelper.
  GetValueSource` on a live button proved `ui:Button`'s Appearance coloring is baked into
  a ControlTemplate trigger with a literal value, never reading those keys. Deleted; 09
  corrected. Brand-coloring Danger/Success/Caution buttons moves to Phase B/C via direct
  `Background`/`Foreground` styles (skip `Appearance=`) — see the plan doc. Verified via
  screenshot tests + pixel sampling in both themes; 258 App tests green (242 passed + 16
  screenshot, skipped without `ADAVOICE_SCREENSHOTS=1`).
- **Phase B (MainWindow) shipped.** State-lit backdrop (4 gradient layers + 3 radial blooms,
  `Opacity`-switched per `Status.State`, all 4 states screenshot-verified in both themes),
  status pill (dot + ALL-CAPS label + tint + static LIVE glow), fixed `148×128` phrase tile
  (2-line title clamp + ellipsis, pill-shaped duration chip, "+1"-cap tag overflow, 5 px
  ribbon), brand-red STOP fill, title-bar hairline (`#2E7D4F → #3E5D3A → #C63C34`). Two
  findings beyond the plan, both pixel-sample/screenshot-confirmed, not assumed: **(1)**
  `Appearance="Primary"` also washes to near-white in both themes (WPF-UI's accent-tint ramp
  assumes a darker base accent than our light brand green) — fixed on MainWindow's Start
  toggle via direct `Background="{DynamicResource Brand.Gradient}"`; **5 other screens still
  need the same fix when Phase C touches them** (Calibration, Recorder, Setup wizard, Repair
  dialog, Phrase edit). **(2)** WPF's `TextBlock` cannot ellipsize a wrapped multi-line clamp
  (`TextTrimming` + `Wrap` silently collapses to single-line — a real, non-obvious WPF gap;
  `MaxLines` doesn't exist either, that's WinUI-only) — fixed via a new `TitleClampConverter`
  that measures against a real off-screen `TextBlock` and truncates the string itself. Full
  detail (including the tile-height/tag-cap corrections found via screenshot, not the
  mockup's literal numbers): [the plan doc](docs/design/plans/brand-redesign-implementation-plan.md).
  A second-opinion review then caught 3 more gaps the fixtures never exercised (populated,
  non-playing, non-broken board only): the playing-tile `Accent` border was missing its
  `Status.Live.Tint` **fill** (09/plan said border **+ fill**; only the border Setter had
  been added) — fixed; the empty-board "Record"/"Record into…" CTAs were still
  `Appearance="Primary"` in the very file whose Start toggle had just been fixed for the
  same bug — fixed via a new shared `BrandCtaButtonStyle`; and the broken-tile
  warning-replaces-tags restructure had never actually been rendered. Added 3 more
  screenshots (playing/broken/empty board) to catch exactly this class of miss going
  forward. **A follow-up look then caught a 4th, related gap**: the tile-clamp stress data
  (a 49-char title + 3rd tag) had been added to `SampleHost()`'s shared `p-1`, distorting
  other dialogs' screenshots that reuse the same fixture (`ManageConversationsDialog`'s row
  text ran behind its buttons) — `Save()` only asserts the PNG exists, not that it looks
  right. Fixed by reverting `p-1` and moving the stress data to a test-local addition; while
  re-verifying, also tightened the title-clamp's measured width (108, not the original 113)
  after a live `ActualWidth` read (110.4) exposed a 2.6 px gap that a longer test string
  turned into a real clipping bug. 270 App tests green (248 passed + 22 screenshot, skipped
  without `ADAVOICE_SCREENSHOTS=1`).

## Latest work (2026-07-17)

- **First `pr-review-toolkit` run + all 10 findings fixed.** Six specialized agents (code-reviewer,
  silent-failure-hunter, type-design-analyzer, code-simplifier, pr-test-analyzer, comment-analyzer)
  reviewed the desktop app in parallel, told to exclude anything the 2026-07-04/07-12 reviews had
  already fixed and focus on code added since (Conversations, phrase versions, UX passes). Full
  report: [reviews/2026-07-17-full-codebase-review.md](docs/reviews/2026-07-17-full-codebase-review.md).
  Top finding, independently surfaced by 3 of the 6 agents: a conversation's "random version"
  playback could pick a version whose WAV was missing and silently play nothing into a live call
  while still advancing the script step — `BoardViewModel.PickVersion` now excludes broken
  versions from the pool, and `IPlaybackHost.PlayEntry` returns an error string (mirroring
  `PreviewEntry`) instead of `void`, so a drop is never silent. Other fixes: export now tells the
  operator when version recordings were dropped; `PhraseLibraryService.Add` falls back to
  Uncategorized for an unknown category id (closing a create/edit asymmetry); the Versions window
  gained a toast channel so a preview failure is no longer swallowed; `AudioEngine.Events`' doc
  comment now states that `PhraseChanged` can fire on the render thread (it omitted that before); a
  false "New version saved" on a since-deleted phrase is now an error; blank rename/save reverts
  the field instead of leaving it diverged from storage; the random-version checkbox and version
  label textbox gate on a new `ILibraryHost.IsWritable` seam member instead of a refused edit being
  swallowed by WPF's binding engine; a cluster of stale "future feature" comments (wizard, WASAPI
  seam) were reworded to match shipped behavior; added tests for the version-vs-primary playback
  selection and the backup-keeps/export-strips version-WAV asymmetry. 508 tests green (was 489) +
  16 skipped; server/ untouched (28 integration tests need a live PostgreSQL, not run here).

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
- **Monetization Phase 1 (domain model + database) shipped (2026-07-13).** 13 EF Core entities
  in `AdaVoice.Server.Domain` (persistence-ignorant POCOs + 10 status enums) and the persistence
  layer in `AdaVoice.Server.Infrastructure`: `AdaVoiceDbContext`, per-entity configs (snake_case,
  UUID v7 keys, `timestamptz` audit columns, `text`+CHECK statuses via value converters, FK
  constraints, citext-based case-insensitive email uniqueness, all §3 indexes), `ITenantProvider`
  global query filters on the 5 tenant-owned tables, a `SaveChanges` interceptor that stamps
  timestamps and tenant_id in one shared place (§14 #16), the `InitialCreate` migration, and an
  idempotent seeder (system tenant + default plan + super_admin, password from env var, never
  logged — §14 #19). Tests: real-PostgreSQL-16 integration (schema, tenant isolation, seeder) run
  in a new CI ubuntu job; DB-less model/guard/enum tests run on Windows. 4 packages added (EF Core
  10 line). `Program.cs` stays inert (no startup migrate/seed wiring this phase). §14 pitfalls
  #16/#18/#19 covered by tests + a source-scan guard; #17 (worker tenant context) recorded as a
  decision, test lands Phase 8.
  Next: Phase 2 (Auth), now unblocked — SEC-03 (lockout enumeration) resolved 2026-07-13:
  the public login endpoint returns the same generic response as a wrong password (no
  `lockedUntil`); `lockedUntil` shows only in the admin panel (see open-questions §9).
  Deferred to Phase 2: the interceptor stamps tenant_id on inserts only; write-path (Attach/
  load-then-modify) tenant enforcement must be added before any write endpoint ships.
- **Monetization Phase 2 (Auth) shipped (2026-07-14).** The `AdaVoice.Server.Api` scaffold became
  a real ASP.NET Core minimal-API host, on the existing Phase-1 schema (**no migration**). Endpoints:
  `POST /api/auth/login` (email+password → ES256 JWT + opaque refresh token), `POST /api/auth/refresh`
  (rotation, single-use), `POST /api/auth/logout`, `POST /api/auth/change-password`, `GET /api/auth/me`.
  Auth orchestration + persistence live in `Infrastructure/Auth` (no ASP.NET types); JWT issuance/
  validation and endpoints live in `Api` (JwtBearer). Highlights: access-token key from an env var
  (`ADAVOICE_JWT_SIGNING_KEY`/`_KID`) — NOT the Phase-5 `signing_keys` table; login resolves the user
  by a filter-bypassed global email lookup (owner-approved MVP; generic failure on 0-or-many);
  refresh rotation uses `SELECT … FOR UPDATE` and revokes the whole family on reuse; lockout counter
  is atomic (`ExecuteUpdate`) and re-arms per window; a request-scoped `HttpContextTenantProvider`
  feeds the Phase-1 query filters from the JWT `tenant_id` claim. §14 pitfalls #1/#2/#3/#4/#5/#6/#20/#22
  each have a test. 3 packages added (JwtBearer + Mvc.Testing + an explicit EF Core pin for version
  hygiene). Integration tests use `WebApplicationFactory` against real PG16, serialized in one xUnit
  collection with a capped Npgsql pool.
  Deferred (later phases): device-binding of refresh tokens (Phase 4), JWKS + `signing_keys` (Phase 5),
  `tenant_suspended` login gating (Phase 3), Serilog sink + `ForwardedHeaders`/`KnownProxies` +
  `/healthz` (Phase 10), Argon2id (Later).

## Done (compact history, newest first)

- ✅ **UI redesign ("Studio Graphite") + light/dark theming, path-traversal fix** (2026-07-11):
  neutral tiles with a category edge marker, light/dark following the OS
  (`ApplicationThemeManager`), all views on `DynamicResource` theme brushes; path-traversal fix
  flattens every `FileName` at the one JSON parse choke point (`LibraryJson.TryParse`). User
  smoke-tested; 238 App + 97 Core tests green.
- ✅ **Phrase-versions bug fixes** (2026-07-08): Recorder now opens over the still-open Versions
  window instead of closing it; a second take in one session no longer misfiles as a new phrase
  (`EndVersionRecordingSession`); added `StopPreview()` so STOP/■/Close all cut preview audio.
  User-verified on the real app; 14 new tests.
- ✅ **Conversations + phrase versions + filter-controls redesign** (2026-07-06/07): Conversations
  entity + `ManageConversationsDialog` + step-highlight; phrase versions (alternate takes) +
  Versions window + per-conversation random-version playback; Category filter became real
  multi-select, both filters moved to compact menu buttons. User smoke-tested on the real app.
- ✅ **Recorder modal, toast notices, UI redesign** (2026-07-06): Recorder moved to a modal
  window; `Notice` became severity-colored toasts; UI redesign (design 09/10 tokens, status pill,
  `FluentWindow` chrome). User smoke-tested; 364 tests green.
- ✅ **Slice 2 — interaction-state gaps** (2026-07-05): repair dialog for broken phrases,
  category-empty CTA, search Clear + query echo, Recorder Processing state, wizard per-check
  spinner. User smoke-tested ("works great, no bugs detected"); 360 tests green. Monetization
  design docs also written this day — see [`docs/monetize/`](docs/monetize/README.md) and
  [`docs/adr/`](docs/adr/).
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
  confirmed against a real Zoho call. Results were recorded in `spike/PHASE0-RESULTS.md`; the
  spike folder was removed in the 2026-07-17 cleanup (see git history). The exact measured
  latency/AGC numbers were never captured in writing — the gate passed by observation.
- ✅ **Design phase** (2026-06-10): 9 design docs, eng + design reviews cleared,
  24 canonical decisions locked.
- ✅ **Conversations smoke test — PASSED (user).** Full end-to-end pass: create a conversation
  with several phrases, reorder, select it from the Board, play through in and out of order,
  step highlight follows correctly, delete a phrase mid-conversation.

## Next action

**Pine Signal brand redesign implementation** — Phases A and B shipped (see above).
**Phase C (dialogs + wizard)** of
[the plan](docs/design/plans/brand-redesign-implementation-plan.md) is next, pending
owner screenshot review of Phase B. Phase C must also apply Phase B's `Appearance="Primary"`
fix to the 5 screens it touches that still use it (Calibration, Recorder, Setup wizard,
Repair dialog, Phrase edit). Phase D (motion) follows. Pass 6 (tile sizing) was absorbed
into Phase B; Pass 2b (`ContentDialog`) is best done before/with Phase C.

Then **UI/UX pass + localization** — remaining slices (scope + rationale in
[ui-ux-localization-scope.md](docs/plans/ui-ux-localization-scope.md)):

1. ✅ Settings window — done, smoke-tested.
2. ✅ Interaction-state gaps — done, smoke-tested.
3. **Full/Docked responsive layout** — not started; needs a design decision first (bring back
   the category rail at ≥720 px, or keep dropdown-only and update design 05).
4. **Localization retrofit (UA/PL/EN)** — last, after slice 3's strings exist. All UI strings
   so far are English-only; a `.resx` retrofit is known debt.

Separately, **monetization**: Phases 0, 1, and 2 are done. Phase 3 (tenant/user/subscription core)
is next. OQ-12/OC-06 (device vs per-seat limits) must be answered before Phase 4 (device activation).
Two write-path notes carried into Phase 3: (1) the tenant interceptor stamps `tenant_id` on inserts
only — load-then-modify / `Attach` write-path tenant enforcement must land before any tenant-scoped
write endpoint; (2) a suspended tenant's users must be blocked at login (`tenant_suspended`), which
needs the subscription/tenant state Phase 3 introduces.

**Monetization** — next step is Phase 3 (tenant/user/subscription core) of the
[monetize roadmap](docs/monetize/implementation-roadmap.md).

## Open follow-ups (named so they're not lost)

- **Configurable monitor device:** preview uses the default output as a stand-in; real
  selection comes with the Settings Devices group.
- **Recorder live level meter + no-signal detection:** deferred until live audio-capture
  polling exists; bundle with the Devices group.
- **2nd-capture fallback:** if a driver refuses a second WASAPI capture client, fall back to
  tapping the engine's capture. Watch for it on hardware.
- **Cold-start auto-retry into Degraded:** a failed `Start` currently stays Stopped with the
  error surfaced.
- ✅ **App-wide `CheckBox`/`TextBox`/`ui:TitleBar` text-colour bug — fixed (2026-07-19, during
  Phase C Step 4).** First reported as a `ManageConversationsDialog`-specific "near-illegible"
  light-theme bug; pixel-sampling the rendered PNGs (`System.Drawing`, not eyeballing) corrected
  that diagnosis on both counts. The text was never illegible — it rendered at a legitimate, solid
  dark gray (`#323232`/`#333333`), just a shade lighter than the app's usual near-black
  `Text.Primary` (`#221E17`), which read as "washed out" only by side-by-side comparison. And it
  wasn't dialog-specific: the identical `#323232` showed up in `SettingsWindow`'s and
  `ManageCategoriesDialog`'s `CheckBox`/`TextBox` too, confirmed via the same pixel-sampling —
  it had simply gone unnoticed everywhere, since both shades are dark enough to look "fine" at a
  glance. Root cause: WPF-UI's own default styles for `CheckBox`/`TextBox`/`ui:TitleBar` each set
  an explicit `Foreground` Setter (a *local* value), which wins over the window's *inherited*
  `TextElement.Foreground="Text.Primary"` regardless of both being `DynamicResource` — plain
  `TextBlock` has no such override, so it already rendered correctly everywhere. Fix: three
  implicit `BasedOn` styles in `Theme/Controls.xaml` (`CheckBox`, `TextBox`, `ui:TitleBar`), each
  adding one `Setter Property="Foreground" Value="{DynamicResource Text.Primary}"` — a derived
  style's own Setter always overrides its `BasedOn` parent's Setter for the same property (unlike
  `ListBoxItem`'s selection fill two paragraphs up, which was baked into a `ControlTemplate`
  trigger no external `Style.Setter` could reach at all). Verified via pixel-sampling after the
  fix: every previously-`#323232` location now reads `#221E17`, across `ManageConversationsDialog`,
  `ManageCategoriesDialog`, and `SettingsWindow`, both themes, with no regression on dark theme or
  on placeholder/disabled-state text (`phrase-edit.png` spot-checked).
- ✅ **Screenshot harness `after-light/` dark-theme bug — fixed (2026-07-12, commit `adda0a7`).**
  Root cause: closing a WPF-UI `FluentWindow` resets `ApplicationThemeManager` back to the OS
  theme as a side effect, so the fixture's one-time theme apply at startup only held for the
  first window. Fix: `ScreenshotHarness` now re-applies `WpfAppFixture.Theme` before building
  every window. Verified — `after-light/settings.png` now renders correctly light.
