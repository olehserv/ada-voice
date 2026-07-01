# Setup Wizard UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the "Bucket A" setup wizard from the design spec — environment checks, voice
calibration, hotkey status, instructions, and a first-call confidence card — reusing the
already-tested logic in `AdaVoice.Audio.Setup` and `EngineHost`, with no new audio capability.

**Architecture:** One modal `SetupWizardWindow` with a `ContentControl` + `DataTemplate` per step
type, backed by a `SetupWizardViewModel` that owns Next/Back/Skip/Finish navigation. A new
`ISetupHost` seam (`RunEnvironmentChecks` + `Calibrate`) keeps every step view-model unit-testable
with a fake, matching every other Board view-model. Triggered on first run (owned by the already-
shown `MainWindow`) and re-runnable via a "Setup…" button in the status bar.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), CommunityToolkit.Mvvm, WPF-UI 4.3.0, xUnit.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-02-setup-wizard-ui-design.md` — read it before starting;
  every task below implements one piece of it.
- No screenshots in the instruction step (text only).
- Device selection, live meters, the loopback self-test, VB-CABLE install detection, and the 3
  extra environment checks are **out of scope** — do not add them.
- Every new view-model depends only on `ISetupHost` (or plain constructor values), never the
  concrete `EngineHost` — this is what keeps it unit-testable with `FakePlaybackHost`.
- Follow the existing flat file layout: view-models live directly under
  `src/AdaVoice.App/ViewModels/`, windows/dialogs directly under `src/AdaVoice.App/` — no new
  subfolders (this project does not use them today).
- Run the full suite (`dotnet test --nologo` from the repo root) after every task; all prior tests
  must stay green.

---

### Task 1: `WizardCompleted` setting (persistence plumbing)

**Files:**
- Modify: `src/AdaVoice.Core/Domain/Settings.cs`
- Modify: `src/AdaVoice.Host/ISettingsHost.cs`
- Modify: `src/AdaVoice.Host/EngineHost.cs`
- Modify: `src/AdaVoice.App/ViewModels/SettingsViewModel.cs`
- Modify: `tests/AdaVoice.App.Tests/FakeSettingsHost.cs`
- Modify: `tests/AdaVoice.Core.Tests/Storage/JsonSettingsRepositoryTests.cs`
- Modify: `tests/AdaVoice.App.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Produces: `Settings.WizardCompleted` (bool, init, default false); `ISettingsHost.WizardCompleted
  { get; }` / `void MarkWizardCompleted()`; `SettingsViewModel.WizardCompleted { get; }` /
  `MarkWizardCompleted()`. Later tasks (8, 9) call `SettingsViewModel.MarkWizardCompleted()` and
  read `SettingsViewModel.WizardCompleted`.

- [ ] **Step 1: Write the failing Core test**

Add to `tests/AdaVoice.Core.Tests/Storage/JsonSettingsRepositoryTests.cs`, right after the
`Window_placement_defaults_to_null_and_roundtrips` test:

```csharp
[Fact]
public void Wizard_completed_defaults_to_false_and_roundtrips()
{
    Assert.False(new JsonSettingsRepository(_root).Load().WizardCompleted);

    new JsonSettingsRepository(_root).Save(new Settings { WizardCompleted = true });

    Assert.True(new JsonSettingsRepository(_root).Load().WizardCompleted);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/AdaVoice.Core.Tests --nologo --filter Wizard_completed_defaults_to_false_and_roundtrips`
Expected: FAIL — `CS1061 'Settings' does not contain a definition for 'WizardCompleted'`.

- [ ] **Step 3: Add the Settings field**

In `src/AdaVoice.Core/Domain/Settings.cs`, add after `WindowTop`:

```csharp
    /// <summary>True once the setup wizard has been completed at least once. Drives whether it
    /// auto-shows on startup; false (the default) means "never completed" — show it.</summary>
    public bool WizardCompleted { get; init; }
```

- [ ] **Step 4: Run the Core test again to verify it passes**

Run: `dotnet test tests/AdaVoice.Core.Tests --nologo --filter Wizard_completed_defaults_to_false_and_roundtrips`
Expected: PASS.

- [ ] **Step 5: Add the seam members**

In `src/AdaVoice.Host/ISettingsHost.cs`, add after `SaveWindowPlacement`:

```csharp
    /// <summary>True once the setup wizard has been completed at least once.</summary>
    bool WizardCompleted { get; }

    /// <summary>Mark the setup wizard completed and persist immediately.</summary>
    void MarkWizardCompleted();
```

- [ ] **Step 6: Implement the seam on `EngineHost`**

In `src/AdaVoice.Host/EngineHost.cs`, add after `SaveWindowPlacement`:

```csharp
    /// <summary>True once the setup wizard has been completed at least once.</summary>
    public bool WizardCompleted => _settings.WizardCompleted;

    /// <summary>Mark the setup wizard completed and persist immediately.</summary>
    public void MarkWizardCompleted()
    {
        _settings = _settings with { WizardCompleted = true };
        _settingsRepository.Save(_settings);
    }
```

- [ ] **Step 7: Add the fake**

In `tests/AdaVoice.App.Tests/FakeSettingsHost.cs`, add:

```csharp
    public bool WizardCompleted { get; set; }
    public int MarkWizardCompletedCount { get; private set; }

    public void MarkWizardCompleted()
    {
        WizardCompleted = true;
        MarkWizardCompletedCount++;
    }
```

- [ ] **Step 8: Write the failing `SettingsViewModel` test**

Add to `tests/AdaVoice.App.Tests/SettingsViewModelTests.cs`, after `Window_placement_reads_and_writes_through_the_host`:

```csharp
    [Fact]
    public void Wizard_completed_reads_and_writes_through_the_host()
    {
        var host = new FakeSettingsHost { WizardCompleted = false };
        var vm = new SettingsViewModel(host);
        Assert.False(vm.WizardCompleted);

        vm.MarkWizardCompleted();

        Assert.True(host.WizardCompleted);
        Assert.Equal(1, host.MarkWizardCompletedCount);
    }
```

- [ ] **Step 9: Run it to verify it fails**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter Wizard_completed_reads_and_writes_through_the_host`
Expected: FAIL — `SettingsViewModel` has no `WizardCompleted`/`MarkWizardCompleted`.

- [ ] **Step 10: Add the passthrough**

In `src/AdaVoice.App/ViewModels/SettingsViewModel.cs`, add after `SaveWindowPlacement`:

```csharp
    /// <summary>True once the setup wizard has been completed at least once.</summary>
    public bool WizardCompleted => _settings.WizardCompleted;

    /// <summary>Mark the setup wizard completed and persist immediately.</summary>
    public void MarkWizardCompleted() => _settings.MarkWizardCompleted();
```

- [ ] **Step 11: Run both tests to verify they pass**

Run: `dotnet test tests/AdaVoice.Core.Tests --nologo && dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, all tests (no regressions).

- [ ] **Step 12: Commit**

```bash
git add src/AdaVoice.Core/Domain/Settings.cs src/AdaVoice.Host/ISettingsHost.cs src/AdaVoice.Host/EngineHost.cs src/AdaVoice.App/ViewModels/SettingsViewModel.cs tests/AdaVoice.App.Tests/FakeSettingsHost.cs tests/AdaVoice.Core.Tests/Storage/JsonSettingsRepositoryTests.cs tests/AdaVoice.App.Tests/SettingsViewModelTests.cs
git commit -m "feat(core,host,app): WizardCompleted setting for the setup wizard"
```

---

### Task 2: `ISetupHost` seam

**Files:**
- Create: `src/AdaVoice.Host/ISetupHost.cs`
- Modify: `src/AdaVoice.Host/EngineHost.cs`
- Modify: `tests/AdaVoice.App.Tests/FakePlaybackHost.cs`

**Interfaces:**
- Consumes: `AdaVoice.Audio.Setup.EnvironmentCheck`, `AdaVoice.Audio.Setup.CalibrationResult`
  (already-existing domain types; no changes to them).
- Produces: `ISetupHost.RunEnvironmentChecks(): IReadOnlyList<EnvironmentCheck>`,
  `ISetupHost.Calibrate(int seconds = 5): CalibrationResult`. Tasks 3, 4, 6, 8 depend on this
  interface. `FakePlaybackHost.NextChecks`, `NextCalibrationResult`, `CalibrateThrows` are the test
  knobs later tasks' tests configure.

- [ ] **Step 1: Create the seam interface**

Create `src/AdaVoice.Host/ISetupHost.cs`:

```csharp
using AdaVoice.Audio.Setup;

namespace AdaVoice.Host;

/// <summary>
/// The setup wizard's view into the host: run the environment checks and run voice calibration.
/// Kept behind a seam (like <see cref="IPlaybackHost"/> / <see cref="ISettingsHost"/>) so the
/// wizard's view-models are unit-testable with a fake. <see cref="EngineHost"/> implements it.
/// </summary>
public interface ISetupHost
{
    /// <summary>Run the environment checks against the live audio devices (cable present + at
    /// 48 kHz, default output is not the cable, a mic is present).</summary>
    IReadOnlyList<EnvironmentCheck> RunEnvironmentChecks();

    /// <summary>Record <paramref name="seconds"/> of the mic, measure the reference level, and on
    /// success persist it so the recorder loudness-matches future takes to it. Blocks for the
    /// duration of the recording — callers should run it off the UI thread.</summary>
    CalibrationResult Calibrate(int seconds = 5);
}
```

- [ ] **Step 2: Implement it on `EngineHost`**

In `src/AdaVoice.Host/EngineHost.cs`, change the class declaration line from:

```csharp
public sealed class EngineHost : IDisposable, IPlaybackHost, IRecorderHost, ISettingsHost, ILibraryHost
```

to:

```csharp
public sealed class EngineHost : IDisposable, IPlaybackHost, IRecorderHost, ISettingsHost, ILibraryHost, ISetupHost
```

`RunEnvironmentChecks()` and `Calibrate(int seconds = 5)` already exist on `EngineHost` with
matching signatures — no method body changes needed.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/AdaVoice.Host --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Add the fake**

In `tests/AdaVoice.App.Tests/FakePlaybackHost.cs`:
- Add `using AdaVoice.Audio.Setup;` to the usings at the top.
- Change the class declaration from:

```csharp
internal sealed class FakePlaybackHost : IPlaybackHost, IRecorderHost, ILibraryHost
```

to:

```csharp
internal sealed class FakePlaybackHost : IPlaybackHost, IRecorderHost, ILibraryHost, ISetupHost
```

- Add these members (near the other `IRecorderHost` knobs is a good spot):

```csharp
    // ---- ISetupHost knobs the tests configure or inspect ----
    public IReadOnlyList<EnvironmentCheck> NextChecks { get; set; } = [];
    public CalibrationResult NextCalibrationResult { get; set; } = new(true, 0.05, null);
    public bool CalibrateThrows { get; set; }

    public IReadOnlyList<EnvironmentCheck> RunEnvironmentChecks()
    {
        Calls.Add("RunEnvironmentChecks");
        return NextChecks;
    }

    public CalibrationResult Calibrate(int seconds = 5)
    {
        Calls.Add("Calibrate");
        if (CalibrateThrows)
            throw new InvalidOperationException("mic busy (simulated)");
        return NextCalibrationResult;
    }
```

- [ ] **Step 5: Build the test project to verify it compiles**

Run: `dotnet build tests/AdaVoice.App.Tests --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Run the full App test suite to verify no regressions**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, same test count as before this task (no new tests yet — this task is plumbing).

- [ ] **Step 7: Commit**

```bash
git add src/AdaVoice.Host/ISetupHost.cs src/AdaVoice.Host/EngineHost.cs tests/AdaVoice.App.Tests/FakePlaybackHost.cs
git commit -m "feat(host): ISetupHost seam (RunEnvironmentChecks + Calibrate) for the setup wizard"
```

---

### Task 3: `EnvironmentChecksStepViewModel`

**Files:**
- Create: `src/AdaVoice.App/ViewModels/IWizardStep.cs`
- Create: `src/AdaVoice.App/ViewModels/EnvironmentChecksStepViewModel.cs`
- Create: `tests/AdaVoice.App.Tests/EnvironmentChecksStepViewModelTests.cs`

**Interfaces:**
- Consumes: `ISetupHost` (Task 2), `FakePlaybackHost.NextChecks` (Task 2).
- Produces: `IWizardStep { bool CanAdvance { get; } }` (implemented by every step view-model in
  Tasks 3-5); `EnvironmentChecksStepViewModel(ISetupHost)` with `Checks`, `CanAdvance`,
  `RecheckCommand`. Task 6 (`SetupWizardViewModel`) constructs this as `Steps[0]`.

- [ ] **Step 1: Create the shared step contract**

Create `src/AdaVoice.App/ViewModels/IWizardStep.cs`:

```csharp
using System.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>The contract every setup-wizard step implements so the wizard shell can gate Next/
/// Finish uniformly. Content-only steps (instructions, first-call) always return true; steps with
/// a real check (environment checks, calibration) compute it. Requires
/// <see cref="INotifyPropertyChanged"/> so the wizard shell can react when a step's own state
/// changes <see cref="CanAdvance"/> (e.g. a calibration completing) — every concrete step is an
/// <c>ObservableObject</c>, which already satisfies this.</summary>
public interface IWizardStep : INotifyPropertyChanged
{
    bool CanAdvance { get; }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/AdaVoice.App.Tests/EnvironmentChecksStepViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class EnvironmentChecksStepViewModelTests
{
    private static EnvironmentCheck Pass(string name) => new(name, CheckStatus.Pass, "ok");
    private static EnvironmentCheck Fail(string name) => new(name, CheckStatus.Fail, "bad");

    [Fact]
    public void Runs_checks_on_construction()
    {
        var host = new FakePlaybackHost { NextChecks = [Pass("Cable")] };

        var step = new EnvironmentChecksStepViewModel(host);

        Assert.Equal(["Cable"], step.Checks.Select(c => c.Name));
        Assert.Contains("RunEnvironmentChecks", host.Calls);
    }

    [Fact]
    public void Cannot_advance_when_a_check_fails()
    {
        var host = new FakePlaybackHost { NextChecks = [Pass("A"), Fail("B")] };

        var step = new EnvironmentChecksStepViewModel(host);

        Assert.False(step.CanAdvance);
    }

    [Fact]
    public void Can_advance_when_every_check_passes()
    {
        var host = new FakePlaybackHost { NextChecks = [Pass("A"), Pass("B")] };

        var step = new EnvironmentChecksStepViewModel(host);

        Assert.True(step.CanAdvance);
    }

    [Fact]
    public void No_checks_means_cannot_advance()
    {
        var step = new EnvironmentChecksStepViewModel(new FakePlaybackHost { NextChecks = [] });

        Assert.False(step.CanAdvance);
    }

    [Fact]
    public void Recheck_re_runs_and_updates_can_advance()
    {
        var host = new FakePlaybackHost { NextChecks = [Fail("A")] };
        var step = new EnvironmentChecksStepViewModel(host);
        Assert.False(step.CanAdvance);

        host.NextChecks = [Pass("A")]; // she fixed it
        step.RecheckCommand.Execute(null);

        Assert.True(step.CanAdvance);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter EnvironmentChecksStepViewModelTests`
Expected: FAIL — `EnvironmentChecksStepViewModel` does not exist.

- [ ] **Step 4: Implement it**

Create `src/AdaVoice.App/ViewModels/EnvironmentChecksStepViewModel.cs`:

```csharp
using AdaVoice.Audio.Setup;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: runs the environment checks and gates Next on every check passing.
/// Talks only to <see cref="ISetupHost"/>, so it is unit-testable with a fake.</summary>
public partial class EnvironmentChecksStepViewModel : ObservableObject, IWizardStep
{
    private readonly ISetupHost _setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private IReadOnlyList<EnvironmentCheck> _checks;

    public EnvironmentChecksStepViewModel(ISetupHost setup)
    {
        _setup = setup;
        _checks = setup.RunEnvironmentChecks();
    }

    /// <summary>True only when at least one check ran and every one passed.</summary>
    public bool CanAdvance => Checks.Count > 0 && Checks.All(c => c.Status == CheckStatus.Pass);

    [RelayCommand]
    private void Recheck() => Checks = _setup.RunEnvironmentChecks();
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter EnvironmentChecksStepViewModelTests`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.App/ViewModels/IWizardStep.cs src/AdaVoice.App/ViewModels/EnvironmentChecksStepViewModel.cs tests/AdaVoice.App.Tests/EnvironmentChecksStepViewModelTests.cs
git commit -m "feat(app): EnvironmentChecksStepViewModel for the setup wizard"
```

---

### Task 4: `CalibrationStepViewModel`

**Files:**
- Create: `src/AdaVoice.App/ViewModels/CalibrationStepViewModel.cs`
- Create: `tests/AdaVoice.App.Tests/CalibrationStepViewModelTests.cs`

**Interfaces:**
- Consumes: `ISetupHost` (Task 2), `IWizardStep` (Task 3), `FakePlaybackHost.NextCalibrationResult`
  / `CalibrateThrows` (Task 2).
- Produces: `CalibrationStepViewModel(ISetupHost)` with `IsRecording`, `Result`, `CanAdvance`,
  `CanStart`, `Succeeded`, `HasMessage`, `StartCalibrationCommand` (async). Task 6 constructs this
  as `Steps[1]`; Task 7's view binds all five of its public properties.

- [ ] **Step 1: Write the failing tests**

Create `tests/AdaVoice.App.Tests/CalibrationStepViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class CalibrationStepViewModelTests
{
    [Fact]
    public void Cannot_advance_before_calibrating()
    {
        var step = new CalibrationStepViewModel(new FakePlaybackHost());

        Assert.False(step.CanAdvance);
        Assert.True(step.CanStart);
        Assert.False(step.HasMessage);
    }

    [Fact]
    public async Task Successful_calibration_allows_advancing()
    {
        var host = new FakePlaybackHost { NextCalibrationResult = new CalibrationResult(true, 0.05, null) };
        var step = new CalibrationStepViewModel(host);

        await step.StartCalibrationCommand.ExecuteAsync(null);

        Assert.True(step.CanAdvance);
        Assert.True(step.Succeeded);
        Assert.False(step.IsRecording);
        Assert.Contains("Calibrate", host.Calls);
    }

    [Fact]
    public async Task Too_quiet_calibration_does_not_allow_advancing()
    {
        var host = new FakePlaybackHost
        {
            NextCalibrationResult = new CalibrationResult(false, 0.001, "We barely heard you — move closer to the mic and try again."),
        };
        var step = new CalibrationStepViewModel(host);

        await step.StartCalibrationCommand.ExecuteAsync(null);

        Assert.False(step.CanAdvance);
        Assert.True(step.HasMessage);
        Assert.Equal("We barely heard you — move closer to the mic and try again.", step.Result!.Message);
    }

    [Fact]
    public async Task Retrying_after_a_too_quiet_result_can_succeed()
    {
        var host = new FakePlaybackHost { NextCalibrationResult = new CalibrationResult(false, 0.001, "too quiet") };
        var step = new CalibrationStepViewModel(host);
        await step.StartCalibrationCommand.ExecuteAsync(null);
        Assert.False(step.CanAdvance);

        host.NextCalibrationResult = new CalibrationResult(true, 0.05, null); // she moved closer
        await step.StartCalibrationCommand.ExecuteAsync(null);

        Assert.True(step.CanAdvance);
    }

    [Fact]
    public async Task A_thrown_exception_surfaces_as_a_friendly_message_instead_of_crashing()
    {
        var host = new FakePlaybackHost { CalibrateThrows = true };
        var step = new CalibrationStepViewModel(host);

        await step.StartCalibrationCommand.ExecuteAsync(null);

        Assert.False(step.CanAdvance);
        Assert.False(step.IsRecording);
        Assert.True(step.HasMessage);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter CalibrationStepViewModelTests`
Expected: FAIL — `CalibrationStepViewModel` does not exist.

- [ ] **Step 3: Implement it**

Create `src/AdaVoice.App/ViewModels/CalibrationStepViewModel.cs`:

```csharp
using AdaVoice.Audio.Setup;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: records 5 seconds of the operator's voice and stores the reference
/// level for loudness-matching future takes. <see cref="ISetupHost.Calibrate"/> blocks for the
/// recording duration, so it runs on a background thread (same pattern as
/// <see cref="BoardViewModel.TestOnHeadphonesCommand"/>).</summary>
public partial class CalibrationStepViewModel : ObservableObject, IWizardStep
{
    private readonly ISetupHost _setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(Succeeded))]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private CalibrationResult? _result;

    public CalibrationStepViewModel(ISetupHost setup) => _setup = setup;

    /// <summary>True only after a successful calibration.</summary>
    public bool CanAdvance => Result is { Ok: true };

    /// <summary>The idle "Start" button shows only while not recording.</summary>
    public bool CanStart => !IsRecording;

    /// <summary>The success message shows only after a successful calibration.</summary>
    public bool Succeeded => Result is { Ok: true };

    /// <summary>A retry/error message is present (e.g. "too quiet") and should be shown.</summary>
    public bool HasMessage => Result?.Message is not null;

    [RelayCommand]
    private async Task StartCalibration()
    {
        IsRecording = true;
        Result = null;
        try
        {
            Result = await Task.Run(() => _setup.Calibrate(5));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Result = new CalibrationResult(false, 0, "Could not access the microphone — close anything else using it and try again.");
        }
        finally
        {
            IsRecording = false;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter CalibrationStepViewModelTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.App/ViewModels/CalibrationStepViewModel.cs tests/AdaVoice.App.Tests/CalibrationStepViewModelTests.cs
git commit -m "feat(app): CalibrationStepViewModel for the setup wizard"
```

---

### Task 5: `HotkeyStatusStepViewModel`, `InstructionStepViewModel`, `FirstCallStepViewModel`

**Files:**
- Create: `src/AdaVoice.App/ViewModels/HotkeyStatusStepViewModel.cs`
- Create: `src/AdaVoice.App/ViewModels/InstructionStepViewModel.cs`
- Create: `src/AdaVoice.App/ViewModels/FirstCallStepViewModel.cs`
- Create: `tests/AdaVoice.App.Tests/HotkeyStatusStepViewModelTests.cs`
- Create: `tests/AdaVoice.App.Tests/InstructionStepViewModelTests.cs`
- Create: `tests/AdaVoice.App.Tests/FirstCallStepViewModelTests.cs`

**Interfaces:**
- Consumes: `IWizardStep` (Task 3).
- Produces: `HotkeyStatusStepViewModel(string? activeHotkey)` with `StatusLabel`, `CanAdvance`;
  `InstructionStepViewModel` with `Steps` (`IReadOnlyList<string>`), `CanAdvance`;
  `FirstCallStepViewModel` with `Checklist` (`ObservableCollection<ChecklistItem>`), `CanAdvance`;
  `ChecklistItem(string text)` with `Text`, `IsChecked`. Task 6 constructs these as `Steps[2..4]`.

- [ ] **Step 1: Write the failing tests**

Create `tests/AdaVoice.App.Tests/HotkeyStatusStepViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class HotkeyStatusStepViewModelTests
{
    [Fact]
    public void Reports_the_registered_hotkey()
    {
        var step = new HotkeyStatusStepViewModel("Pause");

        Assert.Equal("Global stop hotkey registered: Pause", step.StatusLabel);
        Assert.True(step.CanAdvance);
    }

    [Fact]
    public void Reports_unavailable_without_blocking()
    {
        var step = new HotkeyStatusStepViewModel(null);

        Assert.Equal("No global stop hotkey available — use the on-screen STOP button.", step.StatusLabel);
        Assert.True(step.CanAdvance);
    }
}
```

Create `tests/AdaVoice.App.Tests/InstructionStepViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class InstructionStepViewModelTests
{
    [Fact]
    public void Always_allows_advancing_and_has_content()
    {
        var step = new InstructionStepViewModel();

        Assert.True(step.CanAdvance);
        Assert.NotEmpty(step.Steps);
    }
}
```

Create `tests/AdaVoice.App.Tests/FirstCallStepViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class FirstCallStepViewModelTests
{
    [Fact]
    public void Always_allows_advancing_and_has_three_checklist_items()
    {
        var step = new FirstCallStepViewModel();

        Assert.True(step.CanAdvance);
        Assert.Equal(3, step.Checklist.Count);
    }

    [Fact]
    public void Checking_an_item_does_not_affect_can_advance()
    {
        var step = new FirstCallStepViewModel();

        step.Checklist[0].IsChecked = true;

        Assert.True(step.CanAdvance); // local-only feedback, never gates
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "HotkeyStatusStepViewModelTests|InstructionStepViewModelTests|FirstCallStepViewModelTests"`
Expected: FAIL — none of the three view-models exist yet.

- [ ] **Step 3: Implement `HotkeyStatusStepViewModel`**

Create `src/AdaVoice.App/ViewModels/HotkeyStatusStepViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: reports the stop-hotkey that <c>MainWindow</c> already registered
/// on load. Informational only — a missing hotkey never blocks progress, since the on-screen STOP
/// button always works.</summary>
public sealed class HotkeyStatusStepViewModel : ObservableObject, IWizardStep
{
    public HotkeyStatusStepViewModel(string? activeHotkey) =>
        StatusLabel = activeHotkey is { } key
            ? $"Global stop hotkey registered: {key}"
            : "No global stop hotkey available — use the on-screen STOP button.";

    public string StatusLabel { get; }

    public bool CanAdvance => true;
}
```

- [ ] **Step 4: Implement `InstructionStepViewModel`**

Create `src/AdaVoice.App/ViewModels/InstructionStepViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: text instructions for pointing Chrome/Zoho's microphone at CABLE
/// Output. Pure content — no logic, always advances. No screenshots in this slice; they can be
/// added later as image assets without restructuring this step.</summary>
public sealed class InstructionStepViewModel : ObservableObject, IWizardStep
{
    public IReadOnlyList<string> Steps { get; } =
    [
        "Open Chrome and go to your call site (e.g. Zoho Meeting or Zoho Voice).",
        "Open the microphone/audio settings for the call.",
        "Set the microphone to \"CABLE Output (VB-Audio Virtual Cable)\".",
        "Continue to the next step to confirm it works with a real test call.",
    ];

    public bool CanAdvance => true;
}
```

- [ ] **Step 5: Implement `FirstCallStepViewModel`**

Create `src/AdaVoice.App/ViewModels/FirstCallStepViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard's final step: a 3-item checklist nudging a real test call before trusting
/// the app on a client call. The checked state is local UI feedback only — never persisted.</summary>
public sealed class FirstCallStepViewModel : ObservableObject, IWizardStep
{
    public ObservableCollection<ChecklistItem> Checklist { get; } =
    [
        new("Call your own phone or a friend through Zoho."),
        new("Play two phrases during that call."),
        new("Confirm they sound natural and the levels match your voice."),
    ];

    public bool CanAdvance => true;
}

/// <summary>One line of the first-call checklist. Its checked state is local-only (not persisted)
/// — it exists to make the operator consciously confirm each step, not to gate anything.</summary>
public sealed partial class ChecklistItem : ObservableObject
{
    public ChecklistItem(string text) => Text = text;

    public string Text { get; }

    [ObservableProperty]
    private bool _isChecked;
}
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "HotkeyStatusStepViewModelTests|InstructionStepViewModelTests|FirstCallStepViewModelTests"`
Expected: PASS, 5 tests total.

- [ ] **Step 7: Commit**

```bash
git add src/AdaVoice.App/ViewModels/HotkeyStatusStepViewModel.cs src/AdaVoice.App/ViewModels/InstructionStepViewModel.cs src/AdaVoice.App/ViewModels/FirstCallStepViewModel.cs tests/AdaVoice.App.Tests/HotkeyStatusStepViewModelTests.cs tests/AdaVoice.App.Tests/InstructionStepViewModelTests.cs tests/AdaVoice.App.Tests/FirstCallStepViewModelTests.cs
git commit -m "feat(app): hotkey status, instructions, and first-call wizard steps"
```

---

### Task 6: `SetupWizardViewModel` (orchestration)

**Files:**
- Create: `src/AdaVoice.App/ViewModels/SetupWizardViewModel.cs`
- Create: `tests/AdaVoice.App.Tests/SetupWizardViewModelTests.cs`

**Interfaces:**
- Consumes: `ISetupHost` (Task 2), all five step view-models (Tasks 3-5).
- Produces: `SetupWizardViewModel(ISetupHost, string? activeHotkey)` with `Steps`, `CurrentStep`,
  `CurrentStepIndex`, `IsFirstStep`, `IsLastStep`, `CanAdvance`, `ShowSkip`, `NextLabel`,
  `Completed`, `event EventHandler? Finished`, `BackCommand`, `NextCommand`, `SkipAnywayCommand`.
  Task 7's window binds to all of these; Task 8's `BoardViewModel.RunSetupCommand` constructs one.

- [ ] **Step 1: Write the failing tests**

Create `tests/AdaVoice.App.Tests/SetupWizardViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class SetupWizardViewModelTests
{
    private static SetupWizardViewModel NewWizard(FakePlaybackHost? host = null, string? hotkey = "Pause") =>
        new(host ?? new FakePlaybackHost { NextChecks = [new EnvironmentCheck("Cable", CheckStatus.Pass, "ok")] }, hotkey);

    [Fact]
    public void Starts_on_the_first_step()
    {
        var wizard = NewWizard();

        Assert.Same(wizard.Steps[0], wizard.CurrentStep);
        Assert.True(wizard.IsFirstStep);
        Assert.False(wizard.IsLastStep);
    }

    [Fact]
    public void Has_five_steps_in_the_designed_order()
    {
        var wizard = NewWizard();

        Assert.Equal(5, wizard.Steps.Count);
        Assert.IsType<EnvironmentChecksStepViewModel>(wizard.Steps[0]);
        Assert.IsType<CalibrationStepViewModel>(wizard.Steps[1]);
        Assert.IsType<HotkeyStatusStepViewModel>(wizard.Steps[2]);
        Assert.IsType<InstructionStepViewModel>(wizard.Steps[3]);
        Assert.IsType<FirstCallStepViewModel>(wizard.Steps[4]);
    }

    [Fact]
    public void Next_advances_when_the_current_step_allows_it()
    {
        var wizard = NewWizard(); // checks pass by default

        wizard.NextCommand.Execute(null);

        Assert.Same(wizard.Steps[1], wizard.CurrentStep);
    }

    [Fact]
    public void Next_is_disabled_when_the_current_step_blocks_it()
    {
        var host = new FakePlaybackHost { NextChecks = [new EnvironmentCheck("Cable", CheckStatus.Fail, "missing")] };
        var wizard = NewWizard(host);

        Assert.False(wizard.NextCommand.CanExecute(null));
    }

    [Fact]
    public void Skip_anyway_advances_even_when_blocked()
    {
        var host = new FakePlaybackHost { NextChecks = [new EnvironmentCheck("Cable", CheckStatus.Fail, "missing")] };
        var wizard = NewWizard(host);
        Assert.True(wizard.ShowSkip);

        wizard.SkipAnywayCommand.Execute(null);

        Assert.Same(wizard.Steps[1], wizard.CurrentStep);
    }

    [Fact]
    public void Back_returns_to_the_previous_step_and_is_disabled_on_the_first()
    {
        var wizard = NewWizard();
        Assert.False(wizard.BackCommand.CanExecute(null));

        wizard.NextCommand.Execute(null);
        Assert.True(wizard.BackCommand.CanExecute(null));

        wizard.BackCommand.Execute(null);
        Assert.Same(wizard.Steps[0], wizard.CurrentStep);
    }

    [Fact]
    public void Next_label_reads_finish_on_the_last_step()
    {
        var wizard = NewWizard();
        Assert.Equal("Next", wizard.NextLabel);

        for (var i = 0; i < wizard.Steps.Count - 1; i++)
            wizard.SkipAnywayCommand.Execute(null);

        Assert.Equal("Finish", wizard.NextLabel);
    }

    [Fact]
    public void Reaching_next_on_the_last_step_completes_the_wizard()
    {
        var wizard = NewWizard();
        for (var i = 0; i < wizard.Steps.Count - 1; i++)
            wizard.SkipAnywayCommand.Execute(null); // fast-forward past any gates

        Assert.True(wizard.IsLastStep);
        Assert.False(wizard.Completed);

        var raised = false;
        wizard.Finished += (_, _) => raised = true;
        wizard.NextCommand.Execute(null);

        Assert.True(wizard.Completed);
        Assert.True(raised);
    }

    [Fact]
    public void Moving_between_steps_never_marks_it_completed()
    {
        var wizard = NewWizard();

        wizard.NextCommand.Execute(null); // just moves to step 2, never finishes

        Assert.False(wizard.Completed);
    }

    [Fact]
    public async Task Can_advance_updates_when_the_current_steps_own_state_changes()
    {
        var host = new FakePlaybackHost { NextCalibrationResult = new CalibrationResult(false, 0.001, "too quiet") };
        var wizard = NewWizard(host);
        wizard.NextCommand.Execute(null); // -> calibration step
        Assert.False(wizard.CanAdvance);

        host.NextCalibrationResult = new CalibrationResult(true, 0.05, null);
        await ((CalibrationStepViewModel)wizard.CurrentStep).StartCalibrationCommand.ExecuteAsync(null);

        Assert.True(wizard.CanAdvance);
        Assert.True(wizard.NextCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter SetupWizardViewModelTests`
Expected: FAIL — `SetupWizardViewModel` does not exist.

- [ ] **Step 3: Implement it**

Create `src/AdaVoice.App/ViewModels/SetupWizardViewModel.cs`:

```csharp
using System.ComponentModel;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Orchestrates the setup wizard: an ordered set of steps, Next/Back/SkipAnyway/Finish navigation,
/// and the completion signal the caller uses to persist "wizard completed". Each step is gated by
/// its own <see cref="IWizardStep.CanAdvance"/>; the wizard does not know what each step checks.
/// </summary>
public partial class SetupWizardViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(NextLabel))]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(ShowSkip))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private int _currentStepIndex;

    public SetupWizardViewModel(ISetupHost setup, string? activeHotkey)
    {
        Steps =
        [
            new EnvironmentChecksStepViewModel(setup),
            new CalibrationStepViewModel(setup),
            new HotkeyStatusStepViewModel(activeHotkey),
            new InstructionStepViewModel(),
            new FirstCallStepViewModel(),
        ];

        foreach (var step in Steps)
            step.PropertyChanged += OnStepPropertyChanged;
    }

    /// <summary>The steps, in wizard order. Fixed for the lifetime of one wizard run.</summary>
    public IReadOnlyList<IWizardStep> Steps { get; }

    /// <summary>The step currently shown. The window's ContentControl binds to this; a
    /// DataTemplate per concrete step type picks the matching view.</summary>
    public IWizardStep CurrentStep => Steps[CurrentStepIndex];

    public bool IsFirstStep => CurrentStepIndex == 0;
    public bool IsLastStep => CurrentStepIndex == Steps.Count - 1;

    /// <summary>"Finish" on the last step, "Next" everywhere else.</summary>
    public string NextLabel => IsLastStep ? "Finish" : "Next";

    /// <summary>True when the current step allows a normal Next/Finish.</summary>
    public bool CanAdvance => CurrentStep.CanAdvance;

    /// <summary>True when Next is blocked — "Skip anyway" is the only way forward.</summary>
    public bool ShowSkip => !CanAdvance;

    /// <summary>True once the wizard reached Finish on the last step — the caller (App composition
    /// root) uses this to persist "wizard completed". False on Back/Cancel/window-close.</summary>
    public bool Completed { get; private set; }

    /// <summary>Raised when Finish is reached from the last step (via Next or SkipAnyway). The
    /// window subscribes to this to set its own DialogResult (a WPF concept this view-model does
    /// not touch).</summary>
    public event EventHandler? Finished;

    private bool CanGoBack() => !IsFirstStep;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentStepIndex--;

    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private void Next()
    {
        if (IsLastStep)
            Finish();
        else
            CurrentStepIndex++;
    }

    /// <summary>Advance regardless of <see cref="CanAdvance"/> — the operator's explicit choice to
    /// proceed with a failed check or a skipped calibration.</summary>
    [RelayCommand]
    private void SkipAnyway()
    {
        if (IsLastStep)
            Finish();
        else
            CurrentStepIndex++;
    }

    private void Finish()
    {
        Completed = true;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, CurrentStep) && e.PropertyName == nameof(IWizardStep.CanAdvance))
        {
            OnPropertyChanged(nameof(CanAdvance));
            OnPropertyChanged(nameof(ShowSkip));
            NextCommand.NotifyCanExecuteChanged();
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter SetupWizardViewModelTests`
Expected: PASS, 10 tests.

- [ ] **Step 5: Run the full App suite to check for regressions**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, all tests (prior + new).

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.App/ViewModels/SetupWizardViewModel.cs tests/AdaVoice.App.Tests/SetupWizardViewModelTests.cs
git commit -m "feat(app): SetupWizardViewModel orchestrates the wizard's steps"
```

---

### Task 7: Setup wizard window and step views (WPF)

**Files:**
- Create: `src/AdaVoice.App/SetupWizardWindow.xaml` + `.xaml.cs`
- Create: `src/AdaVoice.App/EnvironmentChecksStepView.xaml` + `.xaml.cs`
- Create: `src/AdaVoice.App/CalibrationStepView.xaml` + `.xaml.cs`
- Create: `src/AdaVoice.App/HotkeyStatusStepView.xaml` + `.xaml.cs`
- Create: `src/AdaVoice.App/InstructionStepView.xaml` + `.xaml.cs`
- Create: `src/AdaVoice.App/FirstCallStepView.xaml` + `.xaml.cs`
- Modify: `src/AdaVoice.App/Converters.cs`
- Modify: `src/AdaVoice.App/App.xaml`

**Interfaces:**
- Consumes: `SetupWizardViewModel` and all five step view-models (Tasks 3-6);
  `HexToBrushConverter` (already exists in `Converters.cs`, from the Board library UI work).
- Produces: `SetupWizardWindow(DataContext: SetupWizardViewModel)` — a `Window` with `ShowDialog()`
  returning `true` only when `SetupWizardViewModel.Finished` fired. Task 8 constructs and shows
  this window.

There are no unit tests in this task — WPF `Window`/`UserControl` rendering is not unit-testable
in this codebase (same as every prior dialog). Verification is build-clean + manual smoke (Task 9).

- [ ] **Step 1: Add the two check-status converters**

In `src/AdaVoice.App/Converters.cs`, add `using AdaVoice.Audio.Setup;` to the usings at the top,
then add these two classes at the end of the file (after `ContrastTextConverter`):

```csharp
/// <summary>An environment check's pass/fail status → a "✓ Pass"/"✗ Fail" label.</summary>
public sealed class CheckStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CheckStatus.Pass ? "✓ Pass" : "✗ Fail";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>An environment check's pass/fail status → a green/red brush.</summary>
public sealed class CheckStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Pass = Frozen(Color.FromRgb(0x54, 0xD2, 0x62)); // Status.Live
    private static readonly SolidColorBrush Fail = Frozen(Color.FromRgb(0xFF, 0x6B, 0x6B)); // Status.Degraded

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CheckStatus.Pass ? Pass : Fail;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
```

- [ ] **Step 2: Register the converters in `App.xaml`**

In `src/AdaVoice.App/App.xaml`, add after the `ContrastTextConverter` registration:

```xml
            <app:CheckStatusToLabelConverter x:Key="CheckStatusToLabel" />
            <app:CheckStatusToBrushConverter x:Key="CheckStatusToBrush" />
```

- [ ] **Step 3: Create `EnvironmentChecksStepView`**

Create `src/AdaVoice.App/EnvironmentChecksStepView.xaml`:

```xml
<UserControl x:Class="AdaVoice.App.EnvironmentChecksStepView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <StackPanel>
        <TextBlock Text="Environment checks" FontWeight="SemiBold"
                   FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />
        <ItemsControl ItemsSource="{Binding Checks}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Background="{StaticResource Surface.Raised}" CornerRadius="{StaticResource Radius.Control}"
                            Padding="{StaticResource Pad.Control}" Margin="0,0,0,8">
                        <StackPanel>
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
                                <TextBlock Margin="8,0,0,0" Text="{Binding Status, Converter={StaticResource CheckStatusToLabel}}"
                                           Foreground="{Binding Status, Converter={StaticResource CheckStatusToBrush}}" />
                            </StackPanel>
                            <TextBlock Text="{Binding Detail}" Foreground="{StaticResource Text.Secondary}"
                                       Margin="0,4,0,0" TextWrapping="Wrap" />
                        </StackPanel>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <ui:Button Content="Re-check" Appearance="Secondary" Command="{Binding RecheckCommand}"
                   HorizontalAlignment="Left" />
    </StackPanel>
</UserControl>
```

Create `src/AdaVoice.App/EnvironmentChecksStepView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The environment-checks step's view. Its <c>DataContext</c> is an
/// <c>EnvironmentChecksStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class EnvironmentChecksStepView : UserControl
{
    public EnvironmentChecksStepView() => InitializeComponent();
}
```

- [ ] **Step 4: Create `CalibrationStepView`**

Create `src/AdaVoice.App/CalibrationStepView.xaml`:

```xml
<UserControl x:Class="AdaVoice.App.CalibrationStepView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis" />
    </UserControl.Resources>
    <StackPanel>
        <TextBlock Text="Voice calibration" FontWeight="SemiBold"
                   FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />
        <TextBlock TextWrapping="Wrap" Margin="0,0,0,12" Foreground="{StaticResource Text.Secondary}"
                   Text="Speak normally for 5 seconds so AdaVoice can match your recorded phrases to your live voice level." />

        <ui:Button Content="Start" Appearance="Primary" Command="{Binding StartCalibrationCommand}"
                   HorizontalAlignment="Left"
                   Visibility="{Binding CanStart, Converter={StaticResource BoolToVis}}" />

        <TextBlock Text="Recording… speak now" FontWeight="SemiBold" Margin="0,0,0,8"
                   Visibility="{Binding IsRecording, Converter={StaticResource BoolToVis}}" />

        <TextBlock Text="Voice level captured ✓" Foreground="{StaticResource Status.Live}" FontWeight="SemiBold"
                   Visibility="{Binding Succeeded, Converter={StaticResource BoolToVis}}" />

        <StackPanel Visibility="{Binding HasMessage, Converter={StaticResource BoolToVis}}">
            <TextBlock Text="{Binding Result.Message}" Foreground="{StaticResource Status.Degraded}"
                       TextWrapping="Wrap" Margin="0,4,0,8" />
            <ui:Button Content="Try again" Appearance="Secondary" Command="{Binding StartCalibrationCommand}"
                       HorizontalAlignment="Left" />
        </StackPanel>
    </StackPanel>
</UserControl>
```

Create `src/AdaVoice.App/CalibrationStepView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The voice-calibration step's view. Its <c>DataContext</c> is a
/// <c>CalibrationStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class CalibrationStepView : UserControl
{
    public CalibrationStepView() => InitializeComponent();
}
```

- [ ] **Step 5: Create `HotkeyStatusStepView`**

Create `src/AdaVoice.App/HotkeyStatusStepView.xaml`:

```xml
<UserControl x:Class="AdaVoice.App.HotkeyStatusStepView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBlock Text="Stop hotkey" FontWeight="SemiBold"
                   FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />
        <TextBlock Text="{Binding StatusLabel}" TextWrapping="Wrap" Foreground="{StaticResource Text.Secondary}" />
    </StackPanel>
</UserControl>
```

Create `src/AdaVoice.App/HotkeyStatusStepView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The hotkey-status step's view. Its <c>DataContext</c> is a
/// <c>HotkeyStatusStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class HotkeyStatusStepView : UserControl
{
    public HotkeyStatusStepView() => InitializeComponent();
}
```

- [ ] **Step 6: Create `InstructionStepView`**

Create `src/AdaVoice.App/InstructionStepView.xaml`:

```xml
<UserControl x:Class="AdaVoice.App.InstructionStepView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBlock Text="Point Chrome/Zoho at the cable" FontWeight="SemiBold"
                   FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />
        <ItemsControl ItemsSource="{Binding Steps}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding}" TextWrapping="Wrap" Margin="0,0,0,8" />
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</UserControl>
```

Create `src/AdaVoice.App/InstructionStepView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The Chrome/Zoho instruction step's view. Its <c>DataContext</c> is an
/// <c>InstructionStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class InstructionStepView : UserControl
{
    public InstructionStepView() => InitializeComponent();
}
```

- [ ] **Step 7: Create `FirstCallStepView`**

Create `src/AdaVoice.App/FirstCallStepView.xaml`:

```xml
<UserControl x:Class="AdaVoice.App.FirstCallStepView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBlock Text="Before your first client call" FontWeight="SemiBold"
                   FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />
        <TextBlock Text="Make a test call and confirm everything sounds right:"
                   Foreground="{StaticResource Text.Secondary}" TextWrapping="Wrap" Margin="0,0,0,8" />
        <ItemsControl ItemsSource="{Binding Checklist}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <CheckBox Content="{Binding Text}" IsChecked="{Binding IsChecked}" Margin="0,0,0,8" />
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</UserControl>
```

Create `src/AdaVoice.App/FirstCallStepView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The first-call-confidence step's view. Its <c>DataContext</c> is a
/// <c>FirstCallStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class FirstCallStepView : UserControl
{
    public FirstCallStepView() => InitializeComponent();
}
```

- [ ] **Step 8: Create the wizard window shell**

Create `src/AdaVoice.App/SetupWizardWindow.xaml`:

```xml
<Window x:Class="AdaVoice.App.SetupWizardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:vm="clr-namespace:AdaVoice.App.ViewModels"
        xmlns:local="clr-namespace:AdaVoice.App"
        Title="AdaVoice setup"
        Width="480" SizeToContent="Height" MinHeight="320"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False"
        Background="{StaticResource Surface.Window}"
        TextElement.Foreground="{StaticResource Text.Primary}"
        FontFamily="Segoe UI Variable, Segoe UI" FontSize="14">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis" />

        <DataTemplate DataType="{x:Type vm:EnvironmentChecksStepViewModel}">
            <local:EnvironmentChecksStepView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:CalibrationStepViewModel}">
            <local:CalibrationStepView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:HotkeyStatusStepViewModel}">
            <local:HotkeyStatusStepView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:InstructionStepViewModel}">
            <local:InstructionStepView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:FirstCallStepViewModel}">
            <local:FirstCallStepView />
        </DataTemplate>
    </Window.Resources>
    <StackPanel Margin="16">
        <ContentControl Content="{Binding CurrentStep}" />

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <ui:Button Content="Cancel" Appearance="Secondary" IsCancel="True" Margin="0,0,8,0" />
            <ui:Button Content="Back" Appearance="Secondary" Command="{Binding BackCommand}" Margin="0,0,8,0" />
            <ui:Button Content="Skip anyway" Appearance="Secondary" Command="{Binding SkipAnywayCommand}"
                       Visibility="{Binding ShowSkip, Converter={StaticResource BoolToVis}}" Margin="0,0,8,0" />
            <ui:Button Content="{Binding NextLabel}" Appearance="Primary" Command="{Binding NextCommand}" />
        </StackPanel>
    </StackPanel>
</Window>
```

Create `src/AdaVoice.App/SetupWizardWindow.xaml.cs`:

```csharp
using System.Windows;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

/// <summary>Modal setup wizard. Its <c>DataContext</c> is a <see cref="SetupWizardViewModel"/>; the
/// caller reads <see cref="Window.ShowDialog"/>'s result to know whether it was actually finished
/// (true) versus closed early (false/null) — driven by <see cref="SetupWizardViewModel.Finished"/>,
/// never a plain "window closed" signal.</summary>
public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is SetupWizardViewModel vm)
                vm.Finished += (_, _) => DialogResult = true;
        };
    }
}
```

- [ ] **Step 9: Build to verify it compiles clean**

Run: `dotnet build src/AdaVoice.App --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 10: Run the full App test suite to check for regressions**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, all tests (no new tests this task — WPF views are not unit-tested here).

- [ ] **Step 11: Commit**

```bash
git add src/AdaVoice.App/SetupWizardWindow.xaml src/AdaVoice.App/SetupWizardWindow.xaml.cs src/AdaVoice.App/EnvironmentChecksStepView.xaml src/AdaVoice.App/EnvironmentChecksStepView.xaml.cs src/AdaVoice.App/CalibrationStepView.xaml src/AdaVoice.App/CalibrationStepView.xaml.cs src/AdaVoice.App/HotkeyStatusStepView.xaml src/AdaVoice.App/HotkeyStatusStepView.xaml.cs src/AdaVoice.App/InstructionStepView.xaml src/AdaVoice.App/InstructionStepView.xaml.cs src/AdaVoice.App/FirstCallStepView.xaml src/AdaVoice.App/FirstCallStepView.xaml.cs src/AdaVoice.App/Converters.cs src/AdaVoice.App/App.xaml
git commit -m "feat(app): setup wizard window and step views (WPF)"
```

---

### Task 8: `BoardViewModel`/`MainWindow` wiring (re-run entry point)

**Files:**
- Modify: `src/AdaVoice.App/ViewModels/BoardViewModel.cs`
- Modify: `src/AdaVoice.App/MainWindow.xaml.cs`
- Modify: `src/AdaVoice.App/MainWindow.xaml`
- Modify: `src/AdaVoice.App/App.xaml.cs`
- Modify: `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`

**Interfaces:**
- Consumes: `ISetupHost` (Task 2), `SetupWizardViewModel` (Task 6), `SetupWizardWindow` (Task 7),
  `SettingsViewModel.MarkWizardCompleted()` (Task 1).
- Produces: `BoardViewModel` constructor gains `ISetupHost setup` (required, inserted after
  `library`) and two new optional parameters, `Func<string?>? getActiveHotkey` and
  `Action<SetupWizardViewModel>? showSetupWizard`; `BoardViewModel.RunSetupCommand`.
  `MainWindow.ActiveHotkey { get; }` and `MainWindow.ShowSetupWizard(SetupWizardViewModel)`. This
  task also fixes `App.xaml.cs`'s `BoardViewModel` construction call so the **whole solution stays
  green at the end of this task** — Task 9 only adds the first-run auto-trigger on top of it.

- [ ] **Step 1: Write the failing test**

In `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`, update the `NewBoard` helper (near the top of
the file) to pass the fake as the new `setup` argument and add a `getActiveHotkey`/`showSetupWizard`
pass-through:

```csharp
    private static BoardViewModel NewBoard(
        FakePlaybackHost host,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<SetupWizardViewModel>? showSetupWizard = null) =>
        new(host, host, host, host, new StatusViewModel(host), new SettingsViewModel(new FakeSettingsHost()),
            getActiveHotkey: () => "Pause", confirmDelete: confirmDelete, showEditDialog: showEditDialog,
            showManageCategories: showManageCategories, showSetupWizard: showSetupWizard);
```

Then add this test in the "Edit / delete" or a new "Setup" section (near
`Manage_categories_opens_the_manager_then_rebuilds_the_filter_options`):

```csharp
    [Fact]
    public void Run_setup_opens_the_wizard_with_the_current_hotkey_status()
    {
        var host = new FakePlaybackHost();
        SetupWizardViewModel? shown = null;
        var board = NewBoard(host, showSetupWizard: vm => shown = vm);

        board.RunSetupCommand.Execute(null);

        var hotkeyStep = Assert.IsType<HotkeyStatusStepViewModel>(shown!.Steps[2]);
        Assert.Equal("Global stop hotkey registered: Pause", hotkeyStep.StatusLabel);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter Run_setup_opens_the_wizard_with_the_current_hotkey_status`
Expected: FAIL — `BoardViewModel`'s constructor does not accept a 4th positional argument yet
(compile error), or `RunSetupCommand` does not exist.

- [ ] **Step 3: Update `BoardViewModel`**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`:

Add `using AdaVoice.Host;` is already present (it's needed for `ISetupHost` too — no new using
needed since `IPlaybackHost` etc. already come from `AdaVoice.Host`).

Change the field declarations near the top (after `private readonly ILibraryHost _library;`) to
add:

```csharp
    private readonly ISetupHost _setup;
    private readonly Func<string?> _getActiveHotkey;
    private readonly Action<SetupWizardViewModel> _showSetupWizard;
```

Change the constructor signature and body. Replace:

```csharp
    public BoardViewModel(IPlaybackHost playback, IRecorderHost recorder, ILibraryHost library,
        StatusViewModel status, SettingsViewModel settings, Action<Action>? onUiThread = null,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null)
    {
        _playback = playback;
        _recorder = recorder;
        _library = library;
        _onUiThread = onUiThread ?? (action => action()); // default: inline (unit tests)
        _confirmDelete = confirmDelete ?? (_ => true);     // default: confirm (unit tests)
        _showEditDialog = showEditDialog ?? (_ => false);  // default: cancel (unit tests opt in)
        _showManageCategories = showManageCategories ?? (_ => { }); // default: no-op (unit tests)
```

with:

```csharp
    public BoardViewModel(IPlaybackHost playback, IRecorderHost recorder, ILibraryHost library, ISetupHost setup,
        StatusViewModel status, SettingsViewModel settings, Func<string?>? getActiveHotkey = null,
        Action<Action>? onUiThread = null,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<SetupWizardViewModel>? showSetupWizard = null)
    {
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

Add this command near `ManageCategories` (after its closing brace):

```csharp
    /// <summary>Open the setup wizard on demand (re-run entry point). Always builds a fresh wizard
    /// so a re-run never shows stale check results from a previous run.</summary>
    [RelayCommand]
    private void RunSetup() => _showSetupWizard(new SetupWizardViewModel(_setup, _getActiveHotkey()));
```

- [ ] **Step 4: Add `MainWindow.ActiveHotkey` and `ShowSetupWizard`**

In `src/AdaVoice.App/MainWindow.xaml.cs`, add `using AdaVoice.App.ViewModels;` is already present.
Add these two members after the `_hotkeys` field declaration:

```csharp
    /// <summary>The stop hotkey label <see cref="HotkeyService"/> resolved on load ("Pause",
    /// "Ctrl+F12", or null if neither could be registered). Read by the setup wizard's hotkey step.</summary>
    public string? ActiveHotkey => _hotkeys?.ActiveHotkey;
```

Add this method near `ShowManageCategories`:

```csharp
    /// <summary>Show the modal setup wizard. If she reaches Finish (not just closes early), mark
    /// the wizard completed so it does not auto-show again on the next launch.</summary>
    public void ShowSetupWizard(SetupWizardViewModel wizard)
    {
        var window = new SetupWizardWindow { DataContext = wizard, Owner = this };
        if (window.ShowDialog() == true)
            (DataContext as BoardViewModel)?.Settings.MarkWizardCompleted();
    }
```

- [ ] **Step 5: Add the "Setup…" button**

In `src/AdaVoice.App/MainWindow.xaml`, in the status-bar `StackPanel` (the one containing the
`Start`/`Stop engine`/`OFF AIR` buttons), add after the `OFF AIR` button:

```xml
                    <ui:Button Content="Setup…" Appearance="Secondary" Command="{Binding RunSetupCommand}" Margin="8,0,0,0" />
```

- [ ] **Step 6: Fix `App.xaml.cs`'s `BoardViewModel` construction call**

This is what keeps the whole solution buildable at the end of this task — Task 9 only adds the
first-run auto-trigger on top of this, unchanged otherwise. In `src/AdaVoice.App/App.xaml.cs`'s
`OnStartup`, replace:

```csharp
        var window = new MainWindow();
        var board = new BoardViewModel(
            _host, _host, _host, status, settings,
            action => Dispatcher.BeginInvoke(action),
            confirmDelete: window.ConfirmDelete,
            showEditDialog: window.ShowEditDialog,
            showManageCategories: window.ShowManageCategories);

        window.DataContext = board;
        window.Show();
```

with:

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

        window.DataContext = board;
        window.Show(); // triggers OnLoaded: wires Saved/Deleted AND registers the stop hotkey
```

- [ ] **Step 7: Build the whole solution to verify it is green**

Run: `dotnet build src/AdaVoice.App --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 8: Run the test from Step 1 to verify it passes**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter Run_setup_opens_the_wizard_with_the_current_hotkey_status`
Expected: PASS.

- [ ] **Step 9: Run the full App suite to check for regressions**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, all tests.

- [ ] **Step 10: Commit**

```bash
git add src/AdaVoice.App/ViewModels/BoardViewModel.cs src/AdaVoice.App/MainWindow.xaml.cs src/AdaVoice.App/MainWindow.xaml src/AdaVoice.App/App.xaml.cs tests/AdaVoice.App.Tests/BoardViewModelTests.cs
git commit -m "feat(app): re-run entry point for the setup wizard (Board status bar)"
```

At this point the wizard is fully built and reachable via the "Setup…" button, and the whole
solution builds and tests green — it just doesn't auto-show on first run yet (that's Task 9, a
small additive change).

---

### Task 9: First-run wiring, full verification, manual smoke

**Files:**
- Modify: `src/AdaVoice.App/App.xaml.cs`
- Modify: `handoff.md`

**Interfaces:**
- Consumes: everything from Tasks 1-8 (all already wired and green).
- Produces: the first-run auto-trigger — no new public interfaces.

- [ ] **Step 1: Add the first-run trigger**

In `src/AdaVoice.App/App.xaml.cs`'s `OnStartup`, immediately after the `window.Show();` line added
in Task 8, add:

```csharp

        // First run: window.ActiveHotkey is only valid after Show() (OnLoaded already ran).
        if (!settings.WizardCompleted)
            window.ShowSetupWizard(new SetupWizardViewModel(_host, window.ActiveHotkey));
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/AdaVoice.App --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test --nologo`
Expected: PASS — every project (`AdaVoice.Core.Tests`, `AdaVoice.Audio.Tests`,
`AdaVoice.App.Tests`) green, no regressions from before this plan started.

- [ ] **Step 4: Manual smoke — first run**

This cannot be verified by CI; it needs a real launch.

1. If `%LOCALAPPDATA%\AdaVoice\settings.json` exists, back it up, then either delete it or edit out
   the `wizardCompleted` key (it won't exist yet on an existing install, so the wizard should
   auto-show on the very next launch regardless — the check is simply `WizardCompleted == false`,
   which is the default).
2. Run: `dotnet run --project src/AdaVoice.App`
3. Confirm: the Board appears, and the setup wizard opens on top of it automatically.
4. Step through: Environment checks (Re-check works; if a check fails, "Skip anyway" appears and
   advances) → Calibration (Start records 5s, then shows either "Voice level captured ✓" or a
   too-quiet retry message with "Try again") → Hotkey status (shows the registered key or the
   unavailable message) → Instructions (reads the 4 text steps) → First-call card (check off the
   3 items, click Finish).
5. Confirm the wizard closes and the Board is usable.
6. Close and relaunch the app — the wizard must **not** auto-show again (confirm
   `settings.json` now has `"wizardCompleted": true`).

- [ ] **Step 5: Manual smoke — re-run entry point**

1. With the app still running (or after the relaunch from Step 4.6), click **Setup…** in the
   status bar.
2. Confirm the wizard opens fresh (Environment checks re-run live, Calibration starts unstarted —
   not showing a stale result from the first run).
3. Click **Cancel** partway through — confirm the Board is unaffected and `settings.json` still
   says `wizardCompleted: true` (re-running and cancelling must not un-set it).

- [ ] **Step 6: Update `handoff.md`**

Add a new `✅` entry to the "Done" section (near the top, following the existing style of the
Board library UI entries) summarizing: the setup wizard is built and smoke-tested (Bucket A:
environment checks, calibration, hotkey status, instructions, first-call card), triggered on first
run and re-runnable via "Setup…" in the status bar; device selection, live meters, the loopback
self-test, and the 3 extra environment checks remain a v2 follow-up (link the design spec at
`docs/superpowers/specs/2026-07-02-setup-wizard-ui-design.md`). Update the "Next action" section to
point at whatever comes after this (or leave a note that this was the last completed item pending
the next planning session). Update the `_Last updated:_` date at the top.

- [ ] **Step 7: Commit**

```bash
git add src/AdaVoice.App/App.xaml.cs handoff.md
git commit -m "feat(app): wire the setup wizard into startup (first-run trigger)"
```

---

## Self-review notes

- **Spec coverage:** all 5 in-scope steps (environment checks, calibration, hotkey status,
  instructions, first-call card) have a task; the wizard shell, seam, settings flag, first-run
  trigger, and re-run entry point are each covered (Tasks 1, 2, 6, 7, 8, 9). Out-of-scope items
  (device selection, meters, loopback test, extra checks) are explicitly not implemented anywhere.
- **Type consistency checked:** `ISetupHost.RunEnvironmentChecks()`/`Calibrate(int seconds = 5)`
  used identically in Tasks 2, 3, 4, 6; `IWizardStep.CanAdvance` implemented identically by all five
  step view-models (Tasks 3-5); `SetupWizardViewModel`'s `Steps`/`CurrentStep`/`NextCommand`/
  `BackCommand`/`SkipAnywayCommand`/`Finished`/`Completed` used identically in Tasks 7 and 8;
  `BoardViewModel`'s new constructor parameter order (`playback, recorder, library, setup, status,
  settings, getActiveHotkey, onUiThread, ...`) matches between Task 8's `NewBoard` test helper and
  Task 9's `App.xaml.cs` call site.
- **Known risk called out explicitly, not silently accepted:** `Calibrate` opens its own mic
  capture (same as the Recorder already does); Task 4's defensive try/catch turns a busy-mic
  exception into a friendly retry message instead of crashing the wizard, and is unit-tested.
