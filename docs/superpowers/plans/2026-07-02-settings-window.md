# Settings Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Settings window — Levels (duck slider + re-run calibration), Behavior
(always-on-top, retrigger toggle, hotkey status), and Language & Backup (language picker,
export/import, backup status) — reusing existing seams and backend logic, with no new audio
capability.

**Architecture:** One modal `SettingsWindow` with three stacked group sections (no
navigation/wizard flow, unlike the setup wizard), backed by a `SettingsWindowViewModel` composing
three small group view-models (`LevelsSettingsViewModel`, `BehaviorSettingsViewModel`,
`BackupSettingsViewModel`). All three talk only to `ISettingsHost` (extended) and, for
calibration, `ISetupHost` — the same pattern every other Board view-model already uses. File
dialogs, the restart confirmation, and error display are owned by `MainWindow` via injected
delegates, mirroring `confirmDelete`/`showEditDialog`. Triggered via a "Settings…" button next to
"Setup…" in the status bar.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), CommunityToolkit.Mvvm, WPF-UI 4.3.0, xUnit.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-02-settings-window-design.md` — read it before starting;
  every task below implements one piece of it.
- The Devices group (mic/cable/monitor pickers, live meters) is **out of scope** — do not add it.
- True hotkey reassignment (capture-any-key, conflict detection) is **out of scope** — this slice
  only shows which of the two fixed candidates (`Pause`/`Ctrl+F12`) is currently active.
- The retrigger toggle applies on restart only — do not add a live-mutation path to the engine for
  it.
- `EngineHost.ExportLibrary`/`ImportLibrary` already exist and are correct (used today by the
  console host) — do not reimplement them; only declare `Export`/`Import` on `ISettingsHost` and
  forward to the existing methods.
- There is no `AdaVoice.Host` test project — do not create one. `EngineHost`'s new interface
  members are one-line delegations verified by a clean build, matching how `WizardCompleted`/
  `Calibrate`/etc. are already handled.
- Every new view-model depends only on `ISettingsHost` / `ISetupHost` (or plain constructor
  values), never the concrete `EngineHost` — this is what keeps it unit-testable with
  `FakeSettingsHost`/`FakePlaybackHost`.
- Follow the existing flat file layout: view-models live directly under
  `src/AdaVoice.App/ViewModels/`, windows directly under `src/AdaVoice.App/` — no new subfolders.
- Run the full suite (`dotnet test --nologo` from the repo root) after every task; all prior tests
  must stay green.

---

### Task 1: Behavior/Language settings fields (persistence plumbing)

**Files:**
- Modify: `src/AdaVoice.Core/Domain/Settings.cs`
- Modify: `src/AdaVoice.Host/ISettingsHost.cs`
- Modify: `src/AdaVoice.Host/EngineHost.cs`
- Modify: `tests/AdaVoice.Core.Tests/Storage/JsonSettingsRepositoryTests.cs`

**Interfaces:**
- Produces: `Settings.AlwaysOnTop` (bool, default true), `Settings.ReplaceOnRetrigger` (bool,
  default true), `Settings.Language` (string, default "en"); `ISettingsHost.AlwaysOnTop { get; }`
  / `SetAlwaysOnTop(bool)`, `.ReplaceOnRetrigger { get; }` / `SetReplaceOnRetrigger(bool)`,
  `.Language { get; }` / `SetLanguage(string)`. Tasks 4 and 5 consume these through
  `ISettingsHost`.

- [ ] **Step 1: Write the failing Core tests**

Add to `tests/AdaVoice.Core.Tests/Storage/JsonSettingsRepositoryTests.cs`, right after the
`Wizard_completed_defaults_to_false_and_roundtrips` test (around line 66):

```csharp
    [Fact]
    public void Always_on_top_defaults_to_true_and_roundtrips()
    {
        Assert.True(new JsonSettingsRepository(_root).Load().AlwaysOnTop);

        new JsonSettingsRepository(_root).Save(new Settings { AlwaysOnTop = false });

        Assert.False(new JsonSettingsRepository(_root).Load().AlwaysOnTop);
    }

    [Fact]
    public void Replace_on_retrigger_defaults_to_true_and_roundtrips()
    {
        Assert.True(new JsonSettingsRepository(_root).Load().ReplaceOnRetrigger);

        new JsonSettingsRepository(_root).Save(new Settings { ReplaceOnRetrigger = false });

        Assert.False(new JsonSettingsRepository(_root).Load().ReplaceOnRetrigger);
    }

    [Fact]
    public void Language_defaults_to_en_and_roundtrips()
    {
        Assert.Equal("en", new JsonSettingsRepository(_root).Load().Language);

        new JsonSettingsRepository(_root).Save(new Settings { Language = "uk" });

        Assert.Equal("uk", new JsonSettingsRepository(_root).Load().Language);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/AdaVoice.Core.Tests --nologo --filter "Always_on_top_defaults_to_true_and_roundtrips|Replace_on_retrigger_defaults_to_true_and_roundtrips|Language_defaults_to_en_and_roundtrips"`
Expected: FAIL — `CS1061 'Settings' does not contain a definition for 'AlwaysOnTop'` (and similarly
for the other two).

- [ ] **Step 3: Add the Settings fields**

In `src/AdaVoice.Core/Domain/Settings.cs`, add after `WizardCompleted`:

```csharp
    /// <summary>Whether the Board window stays always-on-top. The window itself applies this (a
    /// WPF concept this record does not touch) — this is just the persisted preference. Default
    /// true so an existing settings.json (this field absent) changes nothing for anyone until they
    /// explicitly turn it off — it matches the app's original hardcoded Topmost="True".</summary>
    public bool AlwaysOnTop { get; init; } = true;

    /// <summary>If true, playing a new phrase replaces the one currently playing; if false, the new
    /// trigger is ignored while a phrase is already playing. Read once when the engine builds the
    /// phrase player — changing it takes effect on the next restart. Default true, matching
    /// <c>PhrasePlayerOptions.ReplaceOnRetrigger</c>'s existing default.</summary>
    public bool ReplaceOnRetrigger { get; init; } = true;

    /// <summary>The UI language code ("en", "uk", or "pl"). Applies on restart. Default "en" — the
    /// app is English-only until the localization retrofit lands; choosing another language
    /// persists the choice but does not yet change any displayed text.</summary>
    public string Language { get; init; } = "en";
```

- [ ] **Step 4: Run the Core tests again to verify they pass**

Run: `dotnet test tests/AdaVoice.Core.Tests --nologo --filter "Always_on_top_defaults_to_true_and_roundtrips|Replace_on_retrigger_defaults_to_true_and_roundtrips|Language_defaults_to_en_and_roundtrips"`
Expected: PASS.

- [ ] **Step 5: Add the seam members**

In `src/AdaVoice.Host/ISettingsHost.cs`, add after `MarkWizardCompleted`:

```csharp
    /// <summary>Whether the Board window should stay always-on-top. The window itself applies
    /// this — this seam only carries the persisted preference.</summary>
    bool AlwaysOnTop { get; }

    /// <summary>Set the always-on-top preference and remember it in memory. Does not write to
    /// disk — call <see cref="SaveSettings"/> to persist.</summary>
    void SetAlwaysOnTop(bool value);

    /// <summary>If true (the default), a new phrase trigger replaces the one currently playing; if
    /// false, the new trigger is ignored while a phrase is already playing. Read once when the
    /// engine builds the phrase player, so a change here takes effect on the next restart.</summary>
    bool ReplaceOnRetrigger { get; }

    /// <summary>Set the retrigger preference and remember it in memory. Does not write to disk —
    /// call <see cref="SaveSettings"/> to persist.</summary>
    void SetReplaceOnRetrigger(bool value);

    /// <summary>The UI language code ("en", "uk", or "pl"). Applies on restart — choosing another
    /// language does not change any displayed text until the localization retrofit lands.</summary>
    string Language { get; }

    /// <summary>Set the language preference and remember it in memory. Does not write to disk —
    /// call <see cref="SaveSettings"/> to persist.</summary>
    void SetLanguage(string code);
```

- [ ] **Step 6: Implement the seam on `EngineHost`**

In `src/AdaVoice.Host/EngineHost.cs`, add after `MarkWizardCompleted` (around line 337):

```csharp
    /// <summary>Whether the Board window should stay always-on-top.</summary>
    public bool AlwaysOnTop => _settings.AlwaysOnTop;

    /// <summary>Remember the always-on-top preference in memory. Does not persist.</summary>
    public void SetAlwaysOnTop(bool value) => _settings = _settings with { AlwaysOnTop = value };

    /// <summary>Whether a new phrase trigger replaces the one currently playing.</summary>
    public bool ReplaceOnRetrigger => _settings.ReplaceOnRetrigger;

    /// <summary>Remember the retrigger preference in memory. Does not persist. Takes effect on the
    /// next restart (read by <see cref="PlayerOptionsFromSettings"/> at construction).</summary>
    public void SetReplaceOnRetrigger(bool value) => _settings = _settings with { ReplaceOnRetrigger = value };

    /// <summary>The UI language code.</summary>
    public string Language => _settings.Language;

    /// <summary>Remember the language preference in memory. Does not persist.</summary>
    public void SetLanguage(string code) => _settings = _settings with { Language = code };
```

Then update `PlayerOptionsFromSettings` (around line 103) to read the new field:

```csharp
    private PhrasePlayerOptions PlayerOptionsFromSettings() => new()
    {
        DuckGain = RampGain.DbToLinear(_settings.MicDuckDb),
        DuckRampMs = _settings.DuckRampMs,
        ReplaceOnRetrigger = _settings.ReplaceOnRetrigger,
    };
```

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build src/AdaVoice.Host --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 8: Run the full Core suite to verify no regressions**

Run: `dotnet test tests/AdaVoice.Core.Tests --nologo`
Expected: PASS, all tests (prior + the 3 new ones).

- [ ] **Step 9: Commit**

```bash
git add src/AdaVoice.Core/Domain/Settings.cs src/AdaVoice.Host/ISettingsHost.cs src/AdaVoice.Host/EngineHost.cs tests/AdaVoice.Core.Tests/Storage/JsonSettingsRepositoryTests.cs
git commit -m "feat(core,host): AlwaysOnTop, ReplaceOnRetrigger, and Language settings"
```

---

### Task 2: Export/Import/backup-status seam members

**Files:**
- Modify: `src/AdaVoice.Core/Storage/BackupService.cs`
- Modify: `tests/AdaVoice.Core.Tests/Storage/BackupServiceTests.cs`
- Modify: `src/AdaVoice.Host/ISettingsHost.cs`
- Modify: `src/AdaVoice.Host/EngineHost.cs`
- Modify: `tests/AdaVoice.App.Tests/FakeSettingsHost.cs`

**Interfaces:**
- Consumes: `AdaVoice.Core.Storage.ImportMode`, `ImportResult` (already exist, no changes).
  `EngineHost.ExportLibrary(string)` / `ImportLibrary(string, ImportMode)` (already exist and
  already work — used today by the console host; this task only exposes them through
  `ISettingsHost`).
- Produces: `BackupService.LatestBackupDate(): DateOnly?`; `ISettingsHost.Export(string)`,
  `.Import(string, ImportMode): ImportResult`, `.LastBackupDate: DateOnly?`,
  `.OpenBackupFolder()`. Task 5 (`BackupSettingsViewModel`) consumes all four.

- [ ] **Step 1: Write the failing `BackupService` test**

Add to `tests/AdaVoice.Core.Tests/Storage/BackupServiceTests.cs`, after
`TryReadLatestLibrary_returns_null_when_no_backups_exist` (around line 86):

```csharp
    [Fact]
    public void LatestBackupDate_returns_the_newest_backups_date()
    {
        SeedLibrary("p-1");
        var service = new BackupService(_root);
        service.EnsureDailyBackup(new DateOnly(2026, 6, 1));
        service.EnsureDailyBackup(new DateOnly(2026, 6, 3));

        Assert.Equal(new DateOnly(2026, 6, 3), service.LatestBackupDate());
    }

    [Fact]
    public void LatestBackupDate_returns_null_when_no_backups_exist()
    {
        Assert.Null(new BackupService(_root).LatestBackupDate());
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/AdaVoice.Core.Tests --nologo --filter "LatestBackupDate_returns_the_newest_backups_date|LatestBackupDate_returns_null_when_no_backups_exist"`
Expected: FAIL — `BackupService` has no `LatestBackupDate`.

- [ ] **Step 3: Implement it**

In `src/AdaVoice.Core/Storage/BackupService.cs`, add after `TryReadLatestLibrary` (around line 73):

```csharp
    /// <summary>The date of the newest backup, or null if none exist yet. Used by the Settings
    /// window's backup status readout.</summary>
    public DateOnly? LatestBackupDate()
    {
        var newest = BackupFilesNewestFirst().FirstOrDefault();
        if (newest is null)
            return null;

        var name = Path.GetFileNameWithoutExtension(newest);
        var dateText = name[AdaVoicePaths.BackupFilePrefix.Length..];
        return DateOnly.TryParse(dateText, out var date) ? date : null;
    }
```

- [ ] **Step 4: Run the tests again to verify they pass**

Run: `dotnet test tests/AdaVoice.Core.Tests --nologo --filter "LatestBackupDate_returns_the_newest_backups_date|LatestBackupDate_returns_null_when_no_backups_exist"`
Expected: PASS.

- [ ] **Step 5: Add the seam members**

In `src/AdaVoice.Host/ISettingsHost.cs`, add `using AdaVoice.Core.Storage;` to the usings at the
top, then add after `SetLanguage`:

```csharp
    /// <summary>Export the library (metadata + active phrase WAVs) to a zip.</summary>
    void Export(string destinationZipPath);

    /// <summary>Import a library archive (merge or replace). The in-session library refreshes on
    /// success — the Board's on-screen list does not (see the design spec's "Import refresh"
    /// note), so the caller should tell the operator a restart is needed to see the result.</summary>
    ImportResult Import(string sourceZipPath, ImportMode mode);

    /// <summary>The date of the newest daily backup, or null if none exist yet.</summary>
    DateOnly? LastBackupDate { get; }

    /// <summary>Open the backups folder in the OS file explorer.</summary>
    void OpenBackupFolder();
```

- [ ] **Step 6: Implement the seam on `EngineHost`**

In `src/AdaVoice.Host/EngineHost.cs`, add after `SetLanguage`:

```csharp
    /// <summary>Export the library to a zip — thin delegation to the already-existing
    /// <see cref="ExportLibrary"/> (used today by the console host).</summary>
    public void Export(string destinationZipPath) => ExportLibrary(destinationZipPath);

    /// <summary>Import a library archive — thin delegation to the already-existing
    /// <see cref="ImportLibrary"/> (used today by the console host), which already reloads the
    /// in-session library on success.</summary>
    public ImportResult Import(string sourceZipPath, ImportMode mode) => ImportLibrary(sourceZipPath, mode);

    /// <summary>The date of the newest daily backup, or null if none exist yet.</summary>
    public DateOnly? LastBackupDate => new BackupService(_dataRoot).LatestBackupDate();

    /// <summary>Open the backups folder in the OS file explorer.</summary>
    public void OpenBackupFolder() =>
        Process.Start(new ProcessStartInfo(AdaVoicePaths.BackupsDir(_dataRoot)) { UseShellExecute = true });
```

`Process`/`ProcessStartInfo` are already available via the existing `using System.Diagnostics;` at
the top of the file (used for `Stopwatch`). `BackupService` and `AdaVoicePaths` are already
imported via `using AdaVoice.Core.Storage;`.

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build src/AdaVoice.Host --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 8: Add the fake**

In `tests/AdaVoice.App.Tests/FakeSettingsHost.cs`, add `using AdaVoice.Core.Storage;` to the
usings at the top, then add:

```csharp
    public bool AlwaysOnTop { get; set; } = true;
    public int SetAlwaysOnTopCount { get; private set; }
    public void SetAlwaysOnTop(bool value) { AlwaysOnTop = value; SetAlwaysOnTopCount++; }

    public bool ReplaceOnRetrigger { get; set; } = true;
    public int SetReplaceOnRetriggerCount { get; private set; }
    public void SetReplaceOnRetrigger(bool value) { ReplaceOnRetrigger = value; SetReplaceOnRetriggerCount++; }

    public string Language { get; set; } = "en";
    public void SetLanguage(string code) => Language = code;

    public string? ExportedPath { get; private set; }
    public void Export(string destinationZipPath) => ExportedPath = destinationZipPath;

    public (string Path, ImportMode Mode)? ImportedWith { get; private set; }
    public ImportResult NextImportResult { get; set; } = new(true, 1, 0);
    public ImportResult Import(string sourceZipPath, ImportMode mode)
    {
        ImportedWith = (sourceZipPath, mode);
        return NextImportResult;
    }

    public DateOnly? LastBackupDate { get; set; }
    public int OpenBackupFolderCount { get; private set; }
    public void OpenBackupFolder() => OpenBackupFolderCount++;
```

- [ ] **Step 9: Build the test project and run the full App suite to verify no regressions**

Run: `dotnet build tests/AdaVoice.App.Tests --nologo && dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, same test count as before this task (no new tests yet — this task is plumbing,
like Task 2 of the setup-wizard plan).

- [ ] **Step 10: Commit**

```bash
git add src/AdaVoice.Core/Storage/BackupService.cs tests/AdaVoice.Core.Tests/Storage/BackupServiceTests.cs src/AdaVoice.Host/ISettingsHost.cs src/AdaVoice.Host/EngineHost.cs tests/AdaVoice.App.Tests/FakeSettingsHost.cs
git commit -m "feat(core,host): Export/Import/backup-status seam for the Settings window"
```

---

### Task 3: `LevelsSettingsViewModel` (and remove the duck slider from `SettingsViewModel`)

**Files:**
- Create: `src/AdaVoice.App/ViewModels/LevelsSettingsViewModel.cs`
- Create: `tests/AdaVoice.App.Tests/LevelsSettingsViewModelTests.cs`
- Modify: `src/AdaVoice.App/ViewModels/SettingsViewModel.cs`
- Modify: `tests/AdaVoice.App.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `ISettingsHost` (existing `MicDuckDb`/`SetMicDuckDb`/`SaveSettings`), `ISetupHost`
  (Task 2 of the setup-wizard plan), `CalibrationStepViewModel` (existing, reused unmodified).
- Produces: `LevelsSettingsViewModel(ISettingsHost, ISetupHost)` with `MicDuckDb`, `DuckLabel`,
  `Calibration` (a `CalibrationStepViewModel`), `Commit()`. Task 6 (`SettingsWindowViewModel`)
  constructs this as `Levels`.

**Deliberate trade-off (per the approved spec):** the duck slider moves out of the Board's status
bar entirely — it no longer lives inline, only in this new view-model. This is a real UX change
from what's shipped today (mid-call duck adjustment now needs Settings open), accepted to match
design 05's original layout and avoid two bound views of the same value.

- [ ] **Step 1: Write the failing tests**

Create `tests/AdaVoice.App.Tests/LevelsSettingsViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class LevelsSettingsViewModelTests
{
    [Fact]
    public void Initializes_from_the_host_without_applying_or_saving()
    {
        var host = new FakeSettingsHost { MicDuckDb = -8 };

        var vm = new LevelsSettingsViewModel(host, new FakePlaybackHost());

        Assert.Equal(-8, vm.MicDuckDb);
        Assert.Empty(host.SetCalls); // no spurious apply at startup
        Assert.Equal(0, host.SaveCount);
    }

    [Fact]
    public void Changing_the_level_applies_it_live_but_does_not_save()
    {
        var host = new FakeSettingsHost { MicDuckDb = -12 };
        var vm = new LevelsSettingsViewModel(host, new FakePlaybackHost());

        vm.MicDuckDb = -20;

        Assert.Equal([-20.0], host.SetCalls);
        Assert.Equal(0, host.SaveCount); // persisted only on Commit
    }

    [Fact]
    public void Commit_persists_the_settings()
    {
        var host = new FakeSettingsHost();
        var vm = new LevelsSettingsViewModel(host, new FakePlaybackHost());

        vm.Commit();

        Assert.Equal(1, host.SaveCount);
    }

    [Fact]
    public void DuckLabel_shows_the_rounded_dB()
    {
        var vm = new LevelsSettingsViewModel(new FakeSettingsHost { MicDuckDb = -12 }, new FakePlaybackHost());

        Assert.Equal("-12 dB", vm.DuckLabel);
    }

    [Fact]
    public void Calibration_reuses_the_wizards_step_view_model_against_the_setup_host()
    {
        var setup = new FakePlaybackHost { NextCalibrationResult = new CalibrationResult(true, 0.05, null) };

        var vm = new LevelsSettingsViewModel(new FakeSettingsHost(), setup);

        Assert.False(vm.Calibration.CanAdvance); // hasn't calibrated yet — proves it's wired to setup, not pre-run
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter LevelsSettingsViewModelTests`
Expected: FAIL — `LevelsSettingsViewModel` does not exist.

- [ ] **Step 3: Implement it**

Create `src/AdaVoice.App/ViewModels/LevelsSettingsViewModel.cs`:

```csharp
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Settings window: the Levels group. Owns the mic-duck slider (moved here from the
/// Board's status bar — design 05 places it in Settings) and re-runs voice calibration by reusing
/// the setup wizard's <see cref="CalibrationStepViewModel"/> unchanged.</summary>
public partial class LevelsSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuckLabel))]
    private double _micDuckDb;

    public LevelsSettingsViewModel(ISettingsHost settings, ISetupHost setup)
    {
        _settings = settings;
        // Seed the backing field directly: assigning the property would fire OnMicDuckDbChanged
        // and post a needless duck change at startup.
        _micDuckDb = settings.MicDuckDb;
        Calibration = new CalibrationStepViewModel(setup);
    }

    /// <summary>The duck level as a short label, e.g. "-12 dB".</summary>
    public string DuckLabel => $"{MicDuckDb:F0} dB";

    /// <summary>Re-run voice calibration — the same step view-model and view the setup wizard
    /// uses, with its <c>CanAdvance</c> simply unused here.</summary>
    public CalibrationStepViewModel Calibration { get; }

    /// <summary>Persist the current duck level (call when the slider drag finishes).</summary>
    public void Commit() => _settings.SaveSettings();

    partial void OnMicDuckDbChanged(double value) => _settings.SetMicDuckDb(value);
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter LevelsSettingsViewModelTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Remove the duck slider from `SettingsViewModel`**

Replace the full contents of `src/AdaVoice.App/ViewModels/SettingsViewModel.cs` with:

```csharp
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Board/App-lifecycle settings: the window's remembered placement and whether the setup wizard
/// has been completed. Talks only to <see cref="ISettingsHost"/>, so it is unit-testable with a
/// fake. The mic-duck slider lives in <see cref="LevelsSettingsViewModel"/> (the Settings window)
/// instead — this class is not the Settings screen's view-model.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _settings;

    public SettingsViewModel(ISettingsHost settings) => _settings = settings;

    /// <summary>The window's saved size and position, or null to use the XAML defaults (first run).</summary>
    public WindowPlacement? WindowPlacement => _settings.WindowPlacement;

    /// <summary>Remember and persist the window's size and position (called when the window closes).</summary>
    public void SaveWindowPlacement(double width, double height, double left, double top) =>
        _settings.SaveWindowPlacement(width, height, left, top);

    /// <summary>True once the setup wizard has been completed at least once.</summary>
    public bool WizardCompleted => _settings.WizardCompleted;

    /// <summary>Mark the setup wizard completed and persist immediately.</summary>
    public void MarkWizardCompleted() => _settings.MarkWizardCompleted();
}
```

- [ ] **Step 6: Trim the now-obsolete duck tests from `SettingsViewModelTests.cs`**

In `tests/AdaVoice.App.Tests/SettingsViewModelTests.cs`, delete these four tests (their coverage
now lives in `LevelsSettingsViewModelTests`): `Initializes_from_the_host_without_applying_or_saving`,
`Changing_the_level_applies_it_live_but_does_not_save`, `Commit_persists_the_settings`,
`DuckLabel_shows_the_rounded_dB`. Keep `Window_placement_reads_and_writes_through_the_host` and
`Wizard_completed_reads_and_writes_through_the_host` unchanged. The file should end up containing
only those two tests.

- [ ] **Step 7: Run the full App suite to verify no regressions**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS. `SettingsViewModelTests` now has 2 tests (down from 6); `LevelsSettingsViewModelTests`
adds 5 — net +1 test, no failures.

- [ ] **Step 8: Commit**

```bash
git add src/AdaVoice.App/ViewModels/LevelsSettingsViewModel.cs tests/AdaVoice.App.Tests/LevelsSettingsViewModelTests.cs src/AdaVoice.App/ViewModels/SettingsViewModel.cs tests/AdaVoice.App.Tests/SettingsViewModelTests.cs
git commit -m "feat(app): LevelsSettingsViewModel; move the duck slider out of SettingsViewModel"
```

---

### Task 4: `BehaviorSettingsViewModel`

**Files:**
- Create: `src/AdaVoice.App/ViewModels/BehaviorSettingsViewModel.cs`
- Create: `tests/AdaVoice.App.Tests/BehaviorSettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `ISettingsHost` (Task 1: `AlwaysOnTop`/`SetAlwaysOnTop`, `ReplaceOnRetrigger`/
  `SetReplaceOnRetrigger`, `SaveSettings`).
- Produces: `BehaviorSettingsViewModel(ISettingsHost, string? activeHotkey)` with `AlwaysOnTop`,
  `ReplaceOnRetrigger`, `HotkeyStatus`. Task 6 constructs this as `Behavior`; Task 7's
  `MainWindow.ShowSettings` observes `AlwaysOnTop` changes to apply `Window.Topmost` live.

- [ ] **Step 1: Write the failing tests**

Create `tests/AdaVoice.App.Tests/BehaviorSettingsViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class BehaviorSettingsViewModelTests
{
    [Fact]
    public void Initializes_from_the_host()
    {
        var host = new FakeSettingsHost { AlwaysOnTop = false, ReplaceOnRetrigger = false };

        var vm = new BehaviorSettingsViewModel(host, "Pause");

        Assert.False(vm.AlwaysOnTop);
        Assert.False(vm.ReplaceOnRetrigger);
    }

    [Fact]
    public void Toggling_always_on_top_applies_and_saves_immediately()
    {
        var host = new FakeSettingsHost { AlwaysOnTop = true };
        var vm = new BehaviorSettingsViewModel(host, "Pause");

        vm.AlwaysOnTop = false;

        Assert.False(host.AlwaysOnTop);
        Assert.Equal(1, host.SetAlwaysOnTopCount);
        Assert.Equal(1, host.SaveCount);
    }

    [Fact]
    public void Toggling_retrigger_applies_and_saves_immediately()
    {
        var host = new FakeSettingsHost { ReplaceOnRetrigger = true };
        var vm = new BehaviorSettingsViewModel(host, "Pause");

        vm.ReplaceOnRetrigger = false;

        Assert.False(host.ReplaceOnRetrigger);
        Assert.Equal(1, host.SetReplaceOnRetriggerCount);
        Assert.Equal(1, host.SaveCount);
    }

    [Fact]
    public void Reports_the_registered_hotkey()
    {
        var vm = new BehaviorSettingsViewModel(new FakeSettingsHost(), "Pause");

        Assert.Equal("Global stop hotkey: Pause", vm.HotkeyStatus);
    }

    [Fact]
    public void Reports_unavailable_without_blocking()
    {
        var vm = new BehaviorSettingsViewModel(new FakeSettingsHost(), null);

        Assert.Equal("No global stop hotkey available — use the on-screen STOP button.", vm.HotkeyStatus);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter BehaviorSettingsViewModelTests`
Expected: FAIL — `BehaviorSettingsViewModel` does not exist.

- [ ] **Step 3: Implement it**

Create `src/AdaVoice.App/ViewModels/BehaviorSettingsViewModel.cs`:

```csharp
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Settings window: the Behavior group. Always-on-top and the retrigger toggle both
/// apply and save immediately (a checkbox needs no drag-end debounce, unlike the duck slider);
/// always-on-top takes effect live (the window observes it), the retrigger toggle only on the
/// next restart (it's read once when the engine builds the phrase player). The hotkey status is
/// read-only, set once at construction.</summary>
public partial class BehaviorSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _settings;

    [ObservableProperty]
    private bool _alwaysOnTop;

    [ObservableProperty]
    private bool _replaceOnRetrigger;

    public BehaviorSettingsViewModel(ISettingsHost settings, string? activeHotkey)
    {
        _settings = settings;
        _alwaysOnTop = settings.AlwaysOnTop;
        _replaceOnRetrigger = settings.ReplaceOnRetrigger;
        HotkeyStatus = activeHotkey is { } key
            ? $"Global stop hotkey: {key}"
            : "No global stop hotkey available — use the on-screen STOP button.";
    }

    /// <summary>The currently active stop hotkey, or the unavailable message. Read-only —
    /// reassignment is out of scope for this slice.</summary>
    public string HotkeyStatus { get; }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        _settings.SetAlwaysOnTop(value);
        _settings.SaveSettings();
    }

    partial void OnReplaceOnRetriggerChanged(bool value)
    {
        _settings.SetReplaceOnRetrigger(value);
        _settings.SaveSettings();
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter BehaviorSettingsViewModelTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.App/ViewModels/BehaviorSettingsViewModel.cs tests/AdaVoice.App.Tests/BehaviorSettingsViewModelTests.cs
git commit -m "feat(app): BehaviorSettingsViewModel for the Settings window"
```

---

### Task 5: `BackupSettingsViewModel`

**Files:**
- Create: `src/AdaVoice.App/ViewModels/BackupSettingsViewModel.cs`
- Create: `tests/AdaVoice.App.Tests/BackupSettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `ISettingsHost` (Task 1: `Language`/`SetLanguage`/`SaveSettings`; Task 2: `Export`,
  `Import`, `LastBackupDate`), `AdaVoice.Core.Storage.ImportMode`/`ImportResult`.
- Produces: `BackupSettingsViewModel(ISettingsHost, Func<string?> pickExportPath,
  Func<(string Path, ImportMode Mode)?> pickImportFile, Action confirmAndRestart,
  Action<string> showError, Action<string> showInfo)` with `Language`, `LastBackupDate`,
  `ExportCommand`, `ImportCommand`. Task 6 constructs this as `Backup`.

- [ ] **Step 1: Write the failing tests**

Create `tests/AdaVoice.App.Tests/BackupSettingsViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;
using AdaVoice.Core.Storage;

namespace AdaVoice.App.Tests;

public class BackupSettingsViewModelTests
{
    private static BackupSettingsViewModel NewVm(
        FakeSettingsHost host,
        Func<string?>? pickExportPath = null,
        Func<(string Path, ImportMode Mode)?>? pickImportFile = null,
        Action? confirmAndRestart = null,
        List<string>? errors = null,
        List<string>? infos = null) =>
        new(host,
            pickExportPath ?? (() => null),
            pickImportFile ?? (() => null),
            confirmAndRestart ?? (() => { }),
            errors is null ? (_ => { }) : errors.Add,
            infos is null ? (_ => { }) : infos.Add);

    [Fact]
    public void Initializes_language_and_last_backup_date_from_the_host()
    {
        var host = new FakeSettingsHost { Language = "uk", LastBackupDate = new DateOnly(2026, 7, 1) };

        var vm = NewVm(host);

        Assert.Equal("uk", vm.Language);
        Assert.Equal(new DateOnly(2026, 7, 1), vm.LastBackupDate);
    }

    [Fact]
    public void Changing_language_saves_and_offers_a_restart()
    {
        var host = new FakeSettingsHost { Language = "en" };
        var restarted = false;
        var vm = NewVm(host, confirmAndRestart: () => restarted = true);

        vm.Language = "pl";

        Assert.Equal("pl", host.Language);
        Assert.Equal(1, host.SaveCount);
        Assert.True(restarted);
    }

    [Fact]
    public void Export_uses_the_picked_path()
    {
        var host = new FakeSettingsHost();
        var vm = NewVm(host, pickExportPath: () => @"C:\exports\out.zip");

        vm.ExportCommand.Execute(null);

        Assert.Equal(@"C:\exports\out.zip", host.ExportedPath);
    }

    [Fact]
    public void Export_does_nothing_when_the_picker_is_cancelled()
    {
        var host = new FakeSettingsHost();
        var vm = NewVm(host, pickExportPath: () => null);

        vm.ExportCommand.Execute(null);

        Assert.Null(host.ExportedPath);
    }

    [Fact]
    public void Export_failure_is_surfaced_without_throwing()
    {
        var host = new ThrowingExportSettingsHost();
        var errors = new List<string>();
        var vm = NewVm(host, pickExportPath: () => @"C:\bad\out.zip", errors: errors);

        vm.ExportCommand.Execute(null);

        Assert.Single(errors);
    }

    [Fact]
    public void Import_uses_the_picked_path_and_mode()
    {
        var host = new FakeSettingsHost { NextImportResult = new ImportResult(true, 3, 1) };
        var vm = NewVm(host, pickImportFile: () => (@"C:\imports\in.zip", ImportMode.Merge));

        vm.ImportCommand.Execute(null);

        Assert.Equal((@"C:\imports\in.zip", ImportMode.Merge), host.ImportedWith);
    }

    [Fact]
    public void Import_success_tells_the_operator_a_restart_is_needed()
    {
        var host = new FakeSettingsHost { NextImportResult = new ImportResult(true, 3, 1) };
        var infos = new List<string>();
        var vm = NewVm(host, pickImportFile: () => (@"C:\imports\in.zip", ImportMode.Merge), infos: infos);

        vm.ImportCommand.Execute(null);

        Assert.Contains(infos, i => i.Contains("restart", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Import_does_nothing_when_the_picker_is_cancelled()
    {
        var host = new FakeSettingsHost();
        var vm = NewVm(host, pickImportFile: () => null);

        vm.ImportCommand.Execute(null);

        Assert.Null(host.ImportedWith);
    }

    [Fact]
    public void Import_failure_is_surfaced_without_throwing()
    {
        var host = new FakeSettingsHost { NextImportResult = new ImportResult(false, 0, 0, "bad archive") };
        var errors = new List<string>();
        var vm = NewVm(host, pickImportFile: () => (@"C:\bad.zip", ImportMode.Replace), errors: errors);

        vm.ImportCommand.Execute(null);

        Assert.Contains(errors, e => e.Contains("bad archive"));
    }

    // A minimal ISettingsHost that throws on Export, to prove the view-model catches it. Must use
    // `override`, not `new` — BackupSettingsViewModel calls Export through the ISettingsHost
    // interface reference, and only a true override participates in that virtual dispatch; `new`
    // would silently keep running the non-throwing base implementation.
    private sealed class ThrowingExportSettingsHost : FakeSettingsHost
    {
        public override void Export(string destinationZipPath) => throw new IOException("disk full");
    }
}
```

Note: `ThrowingExportSettingsHost` needs `Export` to be overridable. Since `FakeSettingsHost` is
`internal sealed`, adjust its `Export` method to be virtual instead of removing `sealed` from the
whole class (simplest fix, smallest diff) — see Step 3a below, done before the view-model step so
the test compiles.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter BackupSettingsViewModelTests`
Expected: FAIL to compile — `BackupSettingsViewModel` does not exist, and `FakeSettingsHost.Export`
is not overridable yet.

- [ ] **Step 3a: Make `FakeSettingsHost.Export` overridable**

In `tests/AdaVoice.App.Tests/FakeSettingsHost.cs`, change the class from `internal sealed class` to
`internal class` (drop `sealed`), and change the `Export` method (added in Task 2) from:

```csharp
    public void Export(string destinationZipPath) => ExportedPath = destinationZipPath;
```

to:

```csharp
    public virtual void Export(string destinationZipPath) => ExportedPath = destinationZipPath;
```

- [ ] **Step 3b: Implement `BackupSettingsViewModel`**

Create `src/AdaVoice.App/ViewModels/BackupSettingsViewModel.cs`:

```csharp
using AdaVoice.Core.Storage;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>Settings window: the Language &amp; Backup group. Every WPF-specific action (file
/// dialogs, the restart confirmation, error display) is owned by the window via injected
/// delegates, so this view-model stays unit-testable with fakes.</summary>
public partial class BackupSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _settings;
    private readonly Func<string?> _pickExportPath;
    private readonly Func<(string Path, ImportMode Mode)?> _pickImportFile;
    private readonly Action _confirmAndRestart;
    private readonly Action<string> _showError;
    private readonly Action<string> _showInfo;

    [ObservableProperty]
    private string _language;

    public BackupSettingsViewModel(
        ISettingsHost settings,
        Func<string?> pickExportPath,
        Func<(string Path, ImportMode Mode)?> pickImportFile,
        Action confirmAndRestart,
        Action<string> showError,
        Action<string> showInfo)
    {
        _settings = settings;
        _pickExportPath = pickExportPath;
        _pickImportFile = pickImportFile;
        _confirmAndRestart = confirmAndRestart;
        _showError = showError;
        _showInfo = showInfo;
        _language = settings.Language;
        LastBackupDate = settings.LastBackupDate;
    }

    /// <summary>The date of the newest daily backup, or null if none exist yet — read once when
    /// the window opens.</summary>
    public DateOnly? LastBackupDate { get; }

    [RelayCommand]
    private void Export()
    {
        var path = _pickExportPath();
        if (path is null)
            return; // cancelled

        try
        {
            _settings.Export(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _showError($"Could not export: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Import()
    {
        var picked = _pickImportFile();
        if (picked is not { } choice)
            return; // cancelled

        var result = _settings.Import(choice.Path, choice.Mode);
        if (!result.Success)
            _showError($"Could not import: {result.Error}");
        else
            _showInfo($"Imported {result.Added} phrase(s) ({result.Skipped} skipped). " +
                "Restart AdaVoice to see them on your board.");
    }

    [RelayCommand]
    private void OpenBackupFolder() => _settings.OpenBackupFolder();

    partial void OnLanguageChanged(string value)
    {
        _settings.SetLanguage(value);
        _settings.SaveSettings();
        _confirmAndRestart();
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter BackupSettingsViewModelTests`
Expected: PASS, 10 tests.

- [ ] **Step 5: Run the full App suite to verify no regressions**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, all tests (prior + new).

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.App/ViewModels/BackupSettingsViewModel.cs tests/AdaVoice.App.Tests/BackupSettingsViewModelTests.cs tests/AdaVoice.App.Tests/FakeSettingsHost.cs
git commit -m "feat(app): BackupSettingsViewModel (language, export/import, backup status)"
```

---

### Task 6: `SettingsWindowViewModel` (composition)

**Files:**
- Create: `src/AdaVoice.App/ViewModels/SettingsWindowViewModel.cs`
- Create: `tests/AdaVoice.App.Tests/SettingsWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `ISettingsHost`, `ISetupHost`, all three group view-models (Tasks 3-5).
- Produces: `SettingsWindowViewModel(ISettingsHost, ISetupHost, string? activeHotkey,
  Func<string?> pickExportPath, Func<(string,ImportMode)?> pickImportFile,
  Action confirmAndRestart, Action<string> showError, Action<string> showInfo)` with `Levels`,
  `Behavior`, `Backup`. Task 7's `BoardViewModel.RunSettingsCommand` constructs one; the window
  binds to all three.

- [ ] **Step 1: Write the failing tests**

Create `tests/AdaVoice.App.Tests/SettingsWindowViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;
using AdaVoice.Core.Storage;

namespace AdaVoice.App.Tests;

public class SettingsWindowViewModelTests
{
    [Fact]
    public void Builds_all_three_groups_from_the_same_settings_host()
    {
        var settings = new FakeSettingsHost { MicDuckDb = -9, AlwaysOnTop = false, Language = "uk" };
        var setup = new FakePlaybackHost();

        var vm = new SettingsWindowViewModel(
            settings, setup, "Pause",
            pickExportPath: () => null,
            pickImportFile: () => null,
            confirmAndRestart: () => { },
            showError: _ => { },
            showInfo: _ => { });

        Assert.Equal(-9, vm.Levels.MicDuckDb);
        Assert.False(vm.Behavior.AlwaysOnTop);
        Assert.Equal("Pause", vm.Behavior.HotkeyStatus.Split(": ")[1]);
        Assert.Equal("uk", vm.Backup.Language);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter SettingsWindowViewModelTests`
Expected: FAIL — `SettingsWindowViewModel` does not exist.

- [ ] **Step 3: Implement it**

Create `src/AdaVoice.App/ViewModels/SettingsWindowViewModel.cs`:

```csharp
using AdaVoice.Core.Storage;
using AdaVoice.Host;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Composition root for the Settings window: builds the three group view-models the window's
/// three sections bind to, all sharing the same <see cref="ISettingsHost"/>. Owns nothing itself.
/// </summary>
public sealed class SettingsWindowViewModel
{
    public SettingsWindowViewModel(
        ISettingsHost settings,
        ISetupHost setup,
        string? activeHotkey,
        Func<string?> pickExportPath,
        Func<(string Path, ImportMode Mode)?> pickImportFile,
        Action confirmAndRestart,
        Action<string> showError,
        Action<string> showInfo)
    {
        Levels = new LevelsSettingsViewModel(settings, setup);
        Behavior = new BehaviorSettingsViewModel(settings, activeHotkey);
        Backup = new BackupSettingsViewModel(settings, pickExportPath, pickImportFile, confirmAndRestart, showError, showInfo);
    }

    public LevelsSettingsViewModel Levels { get; }
    public BehaviorSettingsViewModel Behavior { get; }
    public BackupSettingsViewModel Backup { get; }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter SettingsWindowViewModelTests`
Expected: PASS, 1 test.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.App/ViewModels/SettingsWindowViewModel.cs tests/AdaVoice.App.Tests/SettingsWindowViewModelTests.cs
git commit -m "feat(app): SettingsWindowViewModel composes the Settings window's groups"
```

---

### Task 7: Wire `BoardViewModel`, build the Settings window, and fix `App.xaml.cs`

This task is intentionally one unit of work, not split further: `BoardViewModel`'s new
`ISettingsHost` parameter, the `MainWindow` methods it needs, the `SettingsWindow` those methods
show, and `App.xaml.cs`'s composition-root fix are all mutually dependent — splitting them would
leave the solution unbuildable between commits, which the Global Constraints above rule out.

**Files:**
- Modify: `src/AdaVoice.App/ViewModels/BoardViewModel.cs`
- Modify: `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`
- Create: `src/AdaVoice.App/SettingsWindow.xaml` + `.xaml.cs`
- Modify: `src/AdaVoice.App/MainWindow.xaml`
- Modify: `src/AdaVoice.App/MainWindow.xaml.cs`
- Modify: `src/AdaVoice.App/App.xaml.cs`

**Interfaces:**
- Consumes: `ISettingsHost` (as a 5th constructor parameter, alongside the existing
  `IPlaybackHost`/`IRecorderHost`/`ILibraryHost`/`ISetupHost`), `SettingsWindowViewModel` and its
  three group view-models (Tasks 3-6), `CalibrationStepView` (existing, reused unmodified).
- Produces: `BoardViewModel.RunSettingsCommand`; `SettingsWindow(DataContext:
  SettingsWindowViewModel)`; `MainWindow.ShowSettings`, `.PickExportPath`, `.PickImportFile`,
  `.ConfirmAndRestart`, `.ShowError`, `.ShowInfo`. This is the last task — after it,
  `src/AdaVoice.App` builds clean end to end.

- [ ] **Step 1: Write the failing test**

In `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`, first update the `NewBoard` helper (around
line 10) to accept the new optional parameters:

```csharp
    private static BoardViewModel NewBoard(
        FakePlaybackHost host,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<SetupWizardViewModel>? showSetupWizard = null,
        ISettingsHost? settingsHost = null,
        Action<SettingsWindowViewModel>? showSettings = null) =>
        new(host, host, host, host, settingsHost ?? new FakeSettingsHost(), new StatusViewModel(host),
            new SettingsViewModel(new FakeSettingsHost()),
            getActiveHotkey: () => "Pause", confirmDelete: confirmDelete, showEditDialog: showEditDialog,
            showManageCategories: showManageCategories, showSetupWizard: showSetupWizard,
            showSettings: showSettings);
```

Add `using AdaVoice.Host;` to the usings at the top of the file (needed for `ISettingsHost`).

Then add this test after the existing setup-wizard test (search the file for `RunSetupCommand` to
find the right neighborhood):

```csharp
    [Fact]
    public void Run_settings_builds_a_window_view_model_from_the_hosts_and_shows_it()
    {
        var host = new FakePlaybackHost();
        SettingsWindowViewModel? shown = null;
        var board = NewBoard(host, settingsHost: new FakeSettingsHost(), showSettings: vm => shown = vm);

        board.RunSettingsCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.Equal("Pause", shown!.Behavior.HotkeyStatus.Split(": ")[1]);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter Run_settings_builds_a_window_view_model_from_the_hosts_and_shows_it`
Expected: FAIL to compile — `BoardViewModel` has no `settingsHost`/`showSettings` parameters or
`RunSettingsCommand`.

- [ ] **Step 3: Add the constructor parameters and field**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, add `using AdaVoice.Core.Storage;` to the
usings at the top (needed for `ImportMode` in the new delegate types).

Change the field block (around line 21-30) from:

```csharp
    private readonly IPlaybackHost _playback;
    private readonly IRecorderHost _recorder;
    private readonly ILibraryHost _library;
    private readonly ISetupHost _setup;
    private readonly Func<string?> _getActiveHotkey;
    private readonly Action<SetupWizardViewModel> _showSetupWizard;
    private readonly Func<PhraseItemViewModel, bool> _confirmDelete;
    private readonly Func<PhraseEditViewModel, bool> _showEditDialog;
    private readonly Action<CategoriesViewModel> _showManageCategories;
    private readonly Action<Action> _onUiThread;
```

to:

```csharp
    private readonly IPlaybackHost _playback;
    private readonly IRecorderHost _recorder;
    private readonly ILibraryHost _library;
    private readonly ISetupHost _setup;
    private readonly ISettingsHost _settingsHost;
    private readonly Func<string?> _getActiveHotkey;
    private readonly Action<SetupWizardViewModel> _showSetupWizard;
    private readonly Action<SettingsWindowViewModel> _showSettings;
    private readonly Func<string?> _pickExportPath;
    private readonly Func<(string Path, ImportMode Mode)?> _pickImportFile;
    private readonly Action _confirmAndRestart;
    private readonly Action<string> _showError;
    private readonly Action<string> _showSettingsInfo;
    private readonly Func<PhraseItemViewModel, bool> _confirmDelete;
    private readonly Func<PhraseEditViewModel, bool> _showEditDialog;
    private readonly Action<CategoriesViewModel> _showManageCategories;
    private readonly Action<Action> _onUiThread;
```

Change the constructor signature (around line 56-62) from:

```csharp
    public BoardViewModel(IPlaybackHost playback, IRecorderHost recorder, ILibraryHost library, ISetupHost setup,
        StatusViewModel status, SettingsViewModel settings, Func<string?>? getActiveHotkey = null,
        Action<Action>? onUiThread = null,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<SetupWizardViewModel>? showSetupWizard = null)
```

to:

```csharp
    public BoardViewModel(IPlaybackHost playback, IRecorderHost recorder, ILibraryHost library, ISetupHost setup,
        ISettingsHost settingsHost, StatusViewModel status, SettingsViewModel settings,
        Func<string?>? getActiveHotkey = null,
        Action<Action>? onUiThread = null,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<SetupWizardViewModel>? showSetupWizard = null,
        Action<SettingsWindowViewModel>? showSettings = null,
        Func<string?>? pickExportPath = null,
        Func<(string Path, ImportMode Mode)?>? pickImportFile = null,
        Action? confirmAndRestart = null,
        Action<string>? showError = null,
        Action<string>? showSettingsInfo = null)
```

Change the constructor body's assignment block (around line 64-73) from:

```csharp
        _playback = playback;
        _recorder = recorder;
        _library = library;
        _setup = setup;
        _getActiveHotkey = getActiveHotkey ?? (() => null); // default: no hotkey (unit tests)
        _onUiThread = onUiThread ?? (action => action()); // default: inline (unit tests)
        _confirmDelete = confirmDelete ?? (_ => true);     // default: confirm (unit tests)
        _showEditDialog = showEditDialog ?? (_ => false);  // default: cancel (unit tests opt in)
        _showManageCategories = showManageCategories ?? (_ => { }); // default: no-op (unit tests)
        _showSetupWizard = showSetupWizard ?? (_ => { });  // default: no-op (unit tests)
```

to:

```csharp
        _playback = playback;
        _recorder = recorder;
        _library = library;
        _setup = setup;
        _settingsHost = settingsHost;
        _getActiveHotkey = getActiveHotkey ?? (() => null); // default: no hotkey (unit tests)
        _onUiThread = onUiThread ?? (action => action()); // default: inline (unit tests)
        _confirmDelete = confirmDelete ?? (_ => true);     // default: confirm (unit tests)
        _showEditDialog = showEditDialog ?? (_ => false);  // default: cancel (unit tests opt in)
        _showManageCategories = showManageCategories ?? (_ => { }); // default: no-op (unit tests)
        _showSetupWizard = showSetupWizard ?? (_ => { });  // default: no-op (unit tests)
        _showSettings = showSettings ?? (_ => { });        // default: no-op (unit tests)
        _pickExportPath = pickExportPath ?? (() => null);  // default: cancelled (unit tests)
        _pickImportFile = pickImportFile ?? (() => null);  // default: cancelled (unit tests)
        _confirmAndRestart = confirmAndRestart ?? (() => { }); // default: no-op (unit tests)
        _showError = showError ?? (_ => { });              // default: no-op (unit tests)
        _showSettingsInfo = showSettingsInfo ?? (_ => { }); // default: no-op (unit tests)
```

- [ ] **Step 4: Add the command**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, add after `RunSetup` (around line 181):

```csharp
    /// <summary>Open the Settings window on demand. Always builds a fresh view-model so a re-open
    /// never shows a stale hotkey status or backup date from a previous open.</summary>
    [RelayCommand]
    private void RunSettings() => _showSettings(new SettingsWindowViewModel(
        _settingsHost, _setup, _getActiveHotkey(), _pickExportPath, _pickImportFile,
        _confirmAndRestart, _showError, _showSettingsInfo));
```

- [ ] **Step 5: Run to verify the new test passes**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter Run_settings_builds_a_window_view_model_from_the_hosts_and_shows_it`
Expected: PASS.

`App.xaml.cs` will not compile yet at this point (it still calls the old 12-parameter
`BoardViewModel` constructor) — continue with the remaining steps below in the same task; do not
build the whole solution or commit until Step 12, once every file in this task is updated together.

No unit tests are added for the remaining steps in this task — WPF `Window` rendering is not
unit-testable in this codebase (same as every prior dialog, including the setup wizard).
Verification for those steps is build-clean + manual smoke (the checklist at the end).

- [ ] **Step 6: Remove the duck slider from `MainWindow.xaml`**

In `src/AdaVoice.App/MainWindow.xaml`, delete this block (the "Mic duck" `StackPanel`, currently
right after the engine-controls `StackPanel` inside the status bar `Border`, around lines 61-73):

```xml
                <!-- Mic duck: how much the live voice drops while a phrase plays. Applies live;
                     the chosen value is saved when the drag ends (DragCompleted/LostFocus). -->
                <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                    <TextBlock Text="Mic duck" VerticalAlignment="Center"
                               Foreground="{StaticResource Text.Secondary}" />
                    <Slider x:Name="DuckSlider" Width="170" Margin="8,0" VerticalAlignment="Center"
                            Minimum="-40" Maximum="0" SmallChange="1"
                            IsSnapToTickEnabled="True" TickFrequency="1"
                            Value="{Binding Settings.MicDuckDb, Mode=TwoWay}"
                            Thumb.DragCompleted="DuckSlider_DragCompleted"
                            LostFocus="DuckSlider_Committed" />
                    <TextBlock Text="{Binding Settings.DuckLabel}" VerticalAlignment="Center" MinWidth="48" />
                </StackPanel>
```

The status bar's `StackPanel` now only contains the engine-controls row — remove the now-redundant
outer `StackPanel` wrapper too. Change:

```xml
        <Border Grid.Row="1" Background="{StaticResource Surface.Raised}" CornerRadius="{StaticResource Radius.Control}"
                Padding="{StaticResource Pad.Control}">
            <StackPanel>
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="{Binding Status.StateLabel}" FontWeight="SemiBold"
                               FontSize="{StaticResource FontSize.SectionTitle}"
                               VerticalAlignment="Center" MinWidth="90" />
                    <ui:Button Content="Start" Appearance="Secondary" Command="{Binding StartEngineCommand}" Margin="8,0,0,0"
                               IsEnabled="{Binding Status.CanStart}" />
                    <ui:Button Content="Stop engine" Appearance="Secondary" Command="{Binding StopEngineCommand}" Margin="8,0,0,0"
                               IsEnabled="{Binding Status.IsEngineRunning}" />
                    <ui:Button Content="OFF AIR" Appearance="Secondary" Command="{Binding ToggleOffAirCommand}" Margin="8,0,0,0"
                               IsEnabled="{Binding Status.IsEngineRunning}" />
                    <ui:Button Content="Setup…" Appearance="Secondary" Command="{Binding RunSetupCommand}" Margin="8,0,0,0" />
                </StackPanel>
            </StackPanel>
        </Border>
```

to:

```xml
        <Border Grid.Row="1" Background="{StaticResource Surface.Raised}" CornerRadius="{StaticResource Radius.Control}"
                Padding="{StaticResource Pad.Control}">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{Binding Status.StateLabel}" FontWeight="SemiBold"
                           FontSize="{StaticResource FontSize.SectionTitle}"
                           VerticalAlignment="Center" MinWidth="90" />
                <ui:Button Content="Start" Appearance="Secondary" Command="{Binding StartEngineCommand}" Margin="8,0,0,0"
                           IsEnabled="{Binding Status.CanStart}" />
                <ui:Button Content="Stop engine" Appearance="Secondary" Command="{Binding StopEngineCommand}" Margin="8,0,0,0"
                           IsEnabled="{Binding Status.IsEngineRunning}" />
                <ui:Button Content="OFF AIR" Appearance="Secondary" Command="{Binding ToggleOffAirCommand}" Margin="8,0,0,0"
                           IsEnabled="{Binding Status.IsEngineRunning}" />
                <ui:Button Content="Setup…" Appearance="Secondary" Command="{Binding RunSetupCommand}" Margin="8,0,0,0" />
                <ui:Button Content="Settings…" Appearance="Secondary" Command="{Binding RunSettingsCommand}" Margin="8,0,0,0" />
            </StackPanel>
        </Border>
```

Finally, remove the hardcoded `Topmost="True"` from the root `<ui:FluentWindow ...>` tag (line 5)
— it becomes `App.xaml.cs`'s job to set it from the persisted setting. Change:

```xml
        Title="AdaVoice" Topmost="True"
```

to:

```xml
        Title="AdaVoice"
```

- [ ] **Step 7: Remove the now-unused duck handlers from `MainWindow.xaml.cs`**

In `src/AdaVoice.App/MainWindow.xaml.cs`, delete these three methods (the bottom of the file):

```csharp
    // Persist the duck level only when the user finishes adjusting it (mouse drag end / focus loss),
    // so a drag does not write settings.json on every value change. Live apply happens via the binding.
    private void DuckSlider_DragCompleted(object sender, DragCompletedEventArgs e) => CommitSettings();

    private void DuckSlider_Committed(object sender, RoutedEventArgs e) => CommitSettings();

    private void CommitSettings() => (DataContext as BoardViewModel)?.Settings.Commit();
```

Remove the now-unused `using System.Windows.Controls.Primitives;` from the top of the file (it was
only needed for `DragCompletedEventArgs`).

- [ ] **Step 8: Add the new `MainWindow` methods**

In `src/AdaVoice.App/MainWindow.xaml.cs`, add `using AdaVoice.Core.Storage;` and
`using Microsoft.Win32;` to the usings at the top. Then add these six methods after
`ShowSetupWizard` (around line 98):

```csharp
    /// <summary>Show the modal Settings window. Always-on-top changes apply live to this window
    /// as the operator toggles them — <c>Window.Topmost</c> is a WPF concept the view-model does
    /// not touch, so this window applies it on the view-model's behalf.</summary>
    public void ShowSettings(SettingsWindowViewModel vm)
    {
        vm.Behavior.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BehaviorSettingsViewModel.AlwaysOnTop))
                Topmost = vm.Behavior.AlwaysOnTop;
        };

        new SettingsWindow { DataContext = vm, Owner = this }.ShowDialog();
    }

    /// <summary>Ask where to save a library export. Returns null if the operator cancels.</summary>
    public string? PickExportPath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "AdaVoice export (*.zip)|*.zip",
            FileName = $"adavoice-export-{DateTime.Now:yyyy-MM-dd}.zip",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    /// <summary>Ask which archive to import and whether to merge or replace. Returns null if the
    /// operator cancels at either step.</summary>
    public (string Path, ImportMode Mode)? PickImportFile()
    {
        var openDialog = new OpenFileDialog { Filter = "AdaVoice export (*.zip)|*.zip" };
        if (openDialog.ShowDialog(this) != true)
            return null;

        var choice = System.Windows.MessageBox.Show(
            this,
            "Merge with your current library, or replace it entirely?\n\n" +
            "Yes = Merge (keeps your current phrases)\nNo = Replace (your current library is overwritten)",
            "Import library",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);

        return choice switch
        {
            System.Windows.MessageBoxResult.Yes => (openDialog.FileName, ImportMode.Merge),
            System.Windows.MessageBoxResult.No => (openDialog.FileName, ImportMode.Replace),
            _ => null,
        };
    }

    /// <summary>Offer to restart now so a language change takes effect. Fails silently if the
    /// relaunch itself cannot start — the setting is already saved either way, so a failed restart
    /// must never block closing Settings.</summary>
    public void ConfirmAndRestart()
    {
        var restart = System.Windows.MessageBox.Show(
            this,
            "The language change takes effect after a restart. Restart AdaVoice now?",
            "Restart required",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;

        if (!restart)
            return;

        try
        {
            Process.Start(Environment.ProcessPath!);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warning(ex, "Could not restart automatically — the language change applies on the next manual launch");
        }
    }

    /// <summary>Show an error dialog (Export/Import failures).</summary>
    public void ShowError(string message) =>
        System.Windows.MessageBox.Show(this, message, "AdaVoice",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

    /// <summary>Show an informational dialog (the Import-succeeded notice).</summary>
    public void ShowInfo(string message) =>
        System.Windows.MessageBox.Show(this, message, "AdaVoice",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
```

Add `using System.Diagnostics;` to the usings at the top too (for `Process.Start` in
`ConfirmAndRestart` — `Log` already comes from the existing `using Serilog;`).

- [ ] **Step 9: Create `SettingsWindow`**

Create `src/AdaVoice.App/SettingsWindow.xaml`:

```xml
<Window x:Class="AdaVoice.App.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:local="clr-namespace:AdaVoice.App"
        Title="AdaVoice settings"
        Width="420" SizeToContent="Height" MaxHeight="640"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False"
        Background="{StaticResource Surface.Window}"
        TextElement.Foreground="{StaticResource Text.Primary}"
        FontFamily="Segoe UI Variable, Segoe UI" FontSize="14">
    <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="600">
    <StackPanel Margin="16">

        <!-- Levels: the group she touches most often — top of the page (design 05 §4) -->
        <Border Background="{StaticResource Surface.Raised}" CornerRadius="{StaticResource Radius.Control}"
                Padding="{StaticResource Pad.Control}" Margin="0,0,0,12">
            <StackPanel>
                <TextBlock Text="Levels" FontWeight="SemiBold"
                           FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="Mic duck" VerticalAlignment="Center"
                               Foreground="{StaticResource Text.Secondary}" />
                    <Slider Width="170" Margin="8,0" VerticalAlignment="Center"
                            Minimum="-40" Maximum="0" SmallChange="1"
                            IsSnapToTickEnabled="True" TickFrequency="1"
                            Value="{Binding Levels.MicDuckDb, Mode=TwoWay}"
                            Thumb.DragCompleted="DuckSlider_DragCompleted"
                            LostFocus="DuckSlider_Committed" />
                    <TextBlock Text="{Binding Levels.DuckLabel}" VerticalAlignment="Center" MinWidth="48" />
                </StackPanel>
                <Separator Margin="0,12" />
                <local:CalibrationStepView DataContext="{Binding Levels.Calibration}" />
            </StackPanel>
        </Border>

        <!-- Behavior -->
        <Border Background="{StaticResource Surface.Raised}" CornerRadius="{StaticResource Radius.Control}"
                Padding="{StaticResource Pad.Control}" Margin="0,0,0,12">
            <StackPanel DataContext="{Binding Behavior}">
                <TextBlock Text="Behavior" FontWeight="SemiBold"
                           FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />
                <CheckBox Content="Keep the board always on top" IsChecked="{Binding AlwaysOnTop}" Margin="0,0,0,8" />
                <CheckBox Content="A new phrase stops the one currently playing" IsChecked="{Binding ReplaceOnRetrigger}" />
                <TextBlock Text="(applies after restart)" Foreground="{StaticResource Text.Secondary}"
                           FontSize="{StaticResource FontSize.Label}" Margin="20,0,0,8" />
                <TextBlock Text="{Binding HotkeyStatus}" Foreground="{StaticResource Text.Secondary}" TextWrapping="Wrap" />
            </StackPanel>
        </Border>

        <!-- Language & Backup -->
        <Border Background="{StaticResource Surface.Raised}" CornerRadius="{StaticResource Radius.Control}"
                Padding="{StaticResource Pad.Control}" Margin="0,0,0,12">
            <StackPanel DataContext="{Binding Backup}">
                <TextBlock Text="Language &amp; Backup" FontWeight="SemiBold"
                           FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />

                <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                    <TextBlock Text="Language" VerticalAlignment="Center"
                               Foreground="{StaticResource Text.Secondary}" MinWidth="70" />
                    <ComboBox Width="160" SelectedValuePath="Tag" SelectedValue="{Binding Language}">
                        <ComboBoxItem Content="English" Tag="en" />
                        <ComboBoxItem Content="Українська" Tag="uk" />
                        <ComboBoxItem Content="Polski" Tag="pl" />
                    </ComboBox>
                </StackPanel>

                <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                    <ui:Button Content="Export…" Appearance="Secondary" Command="{Binding ExportCommand}" />
                    <ui:Button Content="Import…" Appearance="Secondary" Command="{Binding ImportCommand}" Margin="8,0,0,0" />
                    <ui:Button Content="Open backup folder" Appearance="Secondary" Command="{Binding OpenBackupFolderCommand}" Margin="8,0,0,0" />
                </StackPanel>

                <TextBlock Foreground="{StaticResource Text.Secondary}">
                    <Run Text="Last backup: " />
                    <Run Text="{Binding LastBackupDate, StringFormat='{}{0:yyyy-MM-dd}', TargetNullValue='never'}" />
                </TextBlock>
            </StackPanel>
        </Border>

        <ui:Button Content="Done" Appearance="Secondary" IsCancel="True"
                   HorizontalAlignment="Right" />
    </StackPanel>
    </ScrollViewer>
</Window>
```

Create `src/AdaVoice.App/SettingsWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls.Primitives;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    // Persist the duck level only when the user finishes adjusting it (mouse drag end / focus
    // loss), so a drag does not write settings.json on every value change. Live apply happens via
    // the binding (same pattern the Board's status bar used before the slider moved here).
    private void DuckSlider_DragCompleted(object sender, DragCompletedEventArgs e) => CommitLevels();

    private void DuckSlider_Committed(object sender, RoutedEventArgs e) => CommitLevels();

    private void CommitLevels() => (DataContext as SettingsWindowViewModel)?.Levels.Commit();
}
```

- [ ] **Step 10: Wire `App.xaml.cs`**

In `src/AdaVoice.App/App.xaml.cs`, change the `BoardViewModel` construction (around lines 34-42)
from:

```csharp
        var window = new MainWindow();
        var board = new BoardViewModel(
            _host, _host, _host, _host, status, settings,
            () => window.ActiveHotkey,
            action => Dispatcher.BeginInvoke(action),
            confirmDelete: window.ConfirmDelete,
            showEditDialog: window.ShowEditDialog,
            showManageCategories: window.ShowManageCategories,
            showSetupWizard: window.ShowSetupWizard);
```

to:

```csharp
        var window = new MainWindow { Topmost = _host.AlwaysOnTop };
        var board = new BoardViewModel(
            _host, _host, _host, _host, _host, status, settings,
            () => window.ActiveHotkey,
            action => Dispatcher.BeginInvoke(action),
            confirmDelete: window.ConfirmDelete,
            showEditDialog: window.ShowEditDialog,
            showManageCategories: window.ShowManageCategories,
            showSetupWizard: window.ShowSetupWizard,
            showSettings: window.ShowSettings,
            pickExportPath: window.PickExportPath,
            pickImportFile: window.PickImportFile,
            confirmAndRestart: window.ConfirmAndRestart,
            showError: window.ShowError,
            showSettingsInfo: window.ShowInfo);
```

- [ ] **Step 11: Build the whole solution to verify it compiles**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` across every project.

- [ ] **Step 12: Run the full test suite to verify no regressions**

Run: `dotnet test --nologo`
Expected: PASS, all tests across `AdaVoice.Core.Tests`, `AdaVoice.Audio.Tests`,
`AdaVoice.App.Tests` (including the `Run_settings_builds_a_window_view_model_from_the_hosts_and_shows_it`
test from Step 1, which could not fully verify against a green build until now).

- [ ] **Step 13: Commit**

```bash
git add src/AdaVoice.App/ViewModels/BoardViewModel.cs tests/AdaVoice.App.Tests/BoardViewModelTests.cs src/AdaVoice.App/SettingsWindow.xaml src/AdaVoice.App/SettingsWindow.xaml.cs src/AdaVoice.App/MainWindow.xaml src/AdaVoice.App/MainWindow.xaml.cs src/AdaVoice.App/App.xaml.cs
git commit -m "feat(app): Settings window (levels, behavior, language & backup)"
```

- [ ] **Step 14: Manual smoke test (before considering this slice done)**

Run the app (`dotnet run --project src/AdaVoice.App`) and confirm, in order:

1. The duck slider is gone from the Board's status bar; a "Settings…" button sits next to "Setup…".
2. Opening Settings shows three sections: Levels, Behavior, Language & Backup.
3. The duck slider in Levels moves live and the label updates; closing and reopening Settings
   shows the value persisted.
4. "Start" under Voice calibration re-runs calibration (5-second countdown ring, same as the
   wizard) and shows "Voice level captured ✓" on success.
5. Toggling "Keep the board always on top" immediately changes whether the Board window stays on
   top of other windows.
6. Toggling "A new phrase stops the one currently playing" does **not** change behavior until you
   restart the app — confirm the "(applies after restart)" note is honest by restarting and
   verifying the new behavior takes effect.
7. Changing Language persists (reopen Settings — the picker remembers your choice) and offers a
   restart; declining leaves the app running with no crash; accepting relaunches the app.
8. Export produces a real `.zip` at the chosen path; opening it externally shows `library.json`
   and an `audio/` folder.
9. Import (try both Merge and Replace) against a real exported file shows an "Imported ✓ …
   restart to see them" dialog, and after restarting the app the imported phrases appear on the
   Board.
10. "Open backup folder" opens Explorer at the correct `backups` folder under
    `%LOCALAPPDATA%\AdaVoice`.
11. "Done" closes the Settings window without any error.
