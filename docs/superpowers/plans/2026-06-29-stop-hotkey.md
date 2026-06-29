# Stop Hotkey Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A global `Pause` hotkey (with `Ctrl+F12` fallback) that stops the current phrase even while Chrome is focused.

**Architecture:** A pure-C# `HotkeyService` owns the key policy (try `Pause`, fall back to `Ctrl+F12`) and raises `StopRequested`; it talks to an `IHotkeyRegistrar` seam. The real `Win32HotkeyRegistrar` wraps `RegisterHotKey` + `HwndSource`; a fake drives unit tests. `MainWindow` builds it from the window handle and wires `StopRequested` to the existing `StopCommand` (= `IPlaybackHost.StopPhrase`). No engine or host changes.

**Tech Stack:** .NET 10 / WPF, WPF-UI (Snackbar), CommunityToolkit.Mvvm, xUnit. Win32 `user32.dll` `RegisterHotKey`/`UnregisterHotKey` + `WM_HOTKEY`.

## Global Constraints

- Hotkey stops the **current phrase only** (`StopPhrase`), never the engine (the live mic must keep passing through).
- Default key **`Pause`** (VK 0x13, no modifier); fallback **`Ctrl+F12`** (MOD_CONTROL | VK_F12 0x7B). Use `MOD_NOREPEAT` (0x4000) on both.
- Reassignment UI and live press-to-test are **out of scope** (Settings page / setup wizard slices).
- `App → Core/Host` dependency direction holds; the hotkey lives in `AdaVoice.App` only.
- Spec: `docs/superpowers/specs/2026-06-29-stop-hotkey-design.md`.

---

### Task 1: Hotkey policy core (`HotkeyDef`, `IHotkeyRegistrar`, `HotkeyService`) + tests

**Files:**
- Create: `src/AdaVoice.App/Services/HotkeyDef.cs`
- Create: `src/AdaVoice.App/Services/IHotkeyRegistrar.cs`
- Create: `src/AdaVoice.App/Services/HotkeyService.cs`
- Create: `tests/AdaVoice.App.Tests/FakeHotkeyRegistrar.cs`
- Create: `tests/AdaVoice.App.Tests/HotkeyServiceTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record HotkeyDef(uint Modifiers, uint VirtualKey, string Display)`
  - `public interface IHotkeyRegistrar : IDisposable { bool TryRegister(HotkeyDef def); event EventHandler? Pressed; }`
  - `public sealed class HotkeyService(IHotkeyRegistrar registrar) : IDisposable` with `bool Register()`, `string? ActiveHotkey { get; }`, `event EventHandler? StopRequested`.

- [x] **Step 1: Write the seam + value types (no logic yet)**

Create `src/AdaVoice.App/Services/HotkeyDef.cs`:

```csharp
namespace AdaVoice.App.Services;

/// <summary>A registerable global hotkey: Win32 modifier flags + virtual-key code, plus a label
/// for logging/UI (e.g. "Pause", "Ctrl+F12").</summary>
public sealed record HotkeyDef(uint Modifiers, uint VirtualKey, string Display);
```

Create `src/AdaVoice.App/Services/IHotkeyRegistrar.cs`:

```csharp
namespace AdaVoice.App.Services;

/// <summary>OS seam for a single global hotkey. The real impl wraps Win32 RegisterHotKey; a fake
/// drives tests. Registering again replaces any previous registration.</summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>Try to claim the hotkey system-wide. Returns false if the OS rejects it (conflict).</summary>
    bool TryRegister(HotkeyDef def);

    /// <summary>Raised when the currently-registered hotkey is pressed.</summary>
    event EventHandler? Pressed;
}
```

- [x] **Step 2: Write the failing tests**

Create `tests/AdaVoice.App.Tests/FakeHotkeyRegistrar.cs`:

```csharp
using AdaVoice.App.Services;

namespace AdaVoice.App.Tests;

/// <summary>Test double: records which defs were attempted, fails the ones named in FailFor, and can
/// simulate a key press.</summary>
internal sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
{
    public List<string> Attempts { get; } = [];
    public HashSet<string> FailFor { get; } = [];

    public event EventHandler? Pressed;

    public bool TryRegister(HotkeyDef def)
    {
        Attempts.Add(def.Display);
        return !FailFor.Contains(def.Display);
    }

    public void SimulatePress() => Pressed?.Invoke(this, EventArgs.Empty);

    public void Dispose() { }
}
```

Create `tests/AdaVoice.App.Tests/HotkeyServiceTests.cs`:

```csharp
using AdaVoice.App.Services;

namespace AdaVoice.App.Tests;

public class HotkeyServiceTests
{
    [Fact]
    public void Register_uses_Pause_first_when_it_succeeds()
    {
        var fake = new FakeHotkeyRegistrar();
        var service = new HotkeyService(fake);

        Assert.True(service.Register());
        Assert.Equal("Pause", service.ActiveHotkey);
        Assert.Equal(["Pause"], fake.Attempts); // did not even try the fallback
    }

    [Fact]
    public void Register_falls_back_to_CtrlF12_when_Pause_is_taken()
    {
        var fake = new FakeHotkeyRegistrar { FailFor = { "Pause" } };
        var service = new HotkeyService(fake);

        Assert.True(service.Register());
        Assert.Equal("Ctrl+F12", service.ActiveHotkey);
        Assert.Equal(["Pause", "Ctrl+F12"], fake.Attempts);
    }

    [Fact]
    public void Register_reports_failure_when_all_candidates_are_taken()
    {
        var fake = new FakeHotkeyRegistrar { FailFor = { "Pause", "Ctrl+F12" } };
        var service = new HotkeyService(fake);

        Assert.False(service.Register());
        Assert.Null(service.ActiveHotkey);
        Assert.Equal(["Pause", "Ctrl+F12"], fake.Attempts);
    }

    [Fact]
    public void A_key_press_raises_StopRequested_once()
    {
        var fake = new FakeHotkeyRegistrar();
        var service = new HotkeyService(fake);
        service.Register();
        var count = 0;
        service.StopRequested += (_, _) => count++;

        fake.SimulatePress();

        Assert.Equal(1, count);
    }
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --filter FullyQualifiedName~HotkeyServiceTests`
Expected: FAIL to compile — `HotkeyService` does not exist.

- [x] **Step 4: Implement `HotkeyService`**

Create `src/AdaVoice.App/Services/HotkeyService.cs`:

```csharp
namespace AdaVoice.App.Services;

/// <summary>
/// Owns the stop-hotkey policy: try Pause, then Ctrl+F12, and keep the first the OS accepts. Raises
/// <see cref="StopRequested"/> when the registered hotkey is pressed. Knows nothing about phrases —
/// the window wires StopRequested to the stop action.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    // MOD_NOREPEAT keeps a held key from firing repeatedly; VK_PAUSE = 0x13, VK_F12 = 0x7B.
    private const uint ModNoRepeat = 0x4000;
    private const uint ModControl = 0x0002;

    private static readonly HotkeyDef[] Candidates =
    [
        new(ModNoRepeat, 0x13, "Pause"),
        new(ModControl | ModNoRepeat, 0x7B, "Ctrl+F12"),
    ];

    private readonly IHotkeyRegistrar _registrar;

    public HotkeyService(IHotkeyRegistrar registrar)
    {
        _registrar = registrar;
        _registrar.Pressed += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The label of the hotkey currently registered, or null if none could be.</summary>
    public string? ActiveHotkey { get; private set; }

    /// <summary>Raised when the registered hotkey is pressed.</summary>
    public event EventHandler? StopRequested;

    /// <summary>Try each candidate in order; keep the first the OS accepts. False if all are taken.</summary>
    public bool Register()
    {
        foreach (var def in Candidates)
        {
            if (_registrar.TryRegister(def))
            {
                ActiveHotkey = def.Display;
                return true;
            }
        }

        ActiveHotkey = null;
        return false;
    }

    public void Dispose() => _registrar.Dispose();
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --filter FullyQualifiedName~HotkeyServiceTests`
Expected: PASS (4 tests).

- [x] **Step 6: Commit**

```bash
git add src/AdaVoice.App/Services tests/AdaVoice.App.Tests/FakeHotkeyRegistrar.cs tests/AdaVoice.App.Tests/HotkeyServiceTests.cs
git commit -m "feat(app): stop-hotkey policy core (Pause -> Ctrl+F12 fallback) + tests"
```

---

### Task 2: Win32 registrar (real interop)

**Files:**
- Create: `src/AdaVoice.App/Services/Win32HotkeyRegistrar.cs`

**Interfaces:**
- Consumes: `IHotkeyRegistrar`, `HotkeyDef` (Task 1).
- Produces: `public sealed class Win32HotkeyRegistrar(IntPtr hwnd) : IHotkeyRegistrar`.

This task is Win32 interop that cannot run in CI; it is verified by the runtime smoke check in Task 3. Right-sized as its own task because it is a self-contained, reviewable unit.

- [x] **Step 1: Implement the registrar**

Create `src/AdaVoice.App/Services/Win32HotkeyRegistrar.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AdaVoice.App.Services;

/// <summary>
/// Real <see cref="IHotkeyRegistrar"/>: registers a system-wide hotkey on the window's HWND via Win32
/// RegisterHotKey and raises <see cref="Pressed"/> from the WM_HOTKEY message. Re-registering replaces
/// the previous key. Dispose unregisters and removes the message hook.
/// </summary>
public sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    private const int HotkeyId = 0xADA;     // any app-unique id
    private const int WmHotkey = 0x0312;

    private readonly HwndSource _source;
    private bool _registered;

    public Win32HotkeyRegistrar(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("No HwndSource for the window handle.");
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public bool TryRegister(HotkeyDef def)
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _registered = RegisterHotKey(_source.Handle, HotkeyId, def.Modifiers, def.VirtualKey);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
            UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
```

- [x] **Step 2: Build to verify it compiles**

Run: `dotnet build src/AdaVoice.App --nologo`
Expected: `Build succeeded.`

- [x] **Step 3: Commit**

```bash
git add src/AdaVoice.App/Services/Win32HotkeyRegistrar.cs
git commit -m "feat(app): Win32 global-hotkey registrar (RegisterHotKey + WM_HOTKEY)"
```

---

### Task 3: Wire the hotkey into the Board window

**Files:**
- Modify: `src/AdaVoice.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `HotkeyService`, `Win32HotkeyRegistrar` (Tasks 1–2); `BoardViewModel.StopCommand` (existing); `RootSnackbar` (existing in `MainWindow.xaml`).

- [x] **Step 1: Add the hotkey setup to the window**

Modify `src/AdaVoice.App/MainWindow.xaml.cs`. Add these usings at the top (alongside the existing ones):

```csharp
using System;
using System.Windows.Interop;
using AdaVoice.App.Services;
using Serilog;
```

Add a field inside the class and extend `OnLoaded`, plus a stop handler. The class currently has `OnLoaded` (wires the save toast) and `OnPhraseSaved`. Update `OnLoaded` and add the new members so the class reads:

```csharp
    private HotkeyService? _hotkeys;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BoardViewModel board)
            board.Saved += OnPhraseSaved;

        SetUpStopHotkey();
        Closed += (_, _) => _hotkeys?.Dispose();
    }

    private void SetUpStopHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hotkeys = new HotkeyService(new Win32HotkeyRegistrar(hwnd));
        _hotkeys.StopRequested += (_, _) => (DataContext as BoardViewModel)?.StopCommand.Execute(null);

        if (_hotkeys.Register())
        {
            Log.Information("Stop hotkey registered: {Key}", _hotkeys.ActiveHotkey);
        }
        else
        {
            Log.Warning("Stop hotkey unavailable: Pause and Ctrl+F12 are both taken");
            new Snackbar(RootSnackbar)
            {
                Title = "Stop hotkey unavailable",
                Content = "Use the on-screen STOP button.",
                Appearance = ControlAppearance.Caution,
                Timeout = TimeSpan.FromSeconds(5),
            }.Show();
        }
    }
```

(`Snackbar` and `ControlAppearance` are already imported via the existing `using Wpf.Ui.Controls;`.)

- [x] **Step 2: Build and run the full test suite**

Run: `dotnet test --nologo`
Expected: all suites PASS (Core, Audio, App — App includes the 4 new hotkey tests).

- [x] **Step 3: Runtime smoke — confirm registration at launch**

Run: `dotnet run --project src/AdaVoice.App` (or launch the built exe), let it open, then close it.
Check the newest log under `src/AdaVoice.App/bin/Debug/net10.0-windows/logs/adavoice-*.log`:
Expected: a line `Stop hotkey registered: Pause` (or `Ctrl+F12`).

- [ ] **Step 4: Manual end-to-end (operator/dev, needs the real app)**

Start the engine, play a phrase, click into **Chrome** so the app is not focused, press **`Pause`**:
Expected: the phrase stops (10 ms fade); the live mic keeps working. This is the core requirement and can only be checked by a real keypress.

- [x] **Step 5: Commit**

```bash
git add src/AdaVoice.App/MainWindow.xaml.cs
git commit -m "feat(app): register the global stop hotkey on the Board window"
```

---

## Self-Review

- **Spec coverage:** global registration (Task 2) ✓; Pause + Ctrl+F12 fallback policy (Task 1) ✓; stops current phrase via `StopPhrase`/`StopCommand` (Task 3) ✓; both-fail → log + status notice, on-screen STOP still works (Task 3) ✓; unregister on close (Tasks 2–3) ✓; testable policy behind a seam (Task 1) ✓; out-of-scope items (reassignment, press-to-test) not included ✓.
- **Placeholder scan:** none — every step has full code or an exact command.
- **Type consistency:** `HotkeyDef(Modifiers, VirtualKey, Display)`, `IHotkeyRegistrar.TryRegister`/`Pressed`, `HotkeyService.Register()`/`ActiveHotkey`/`StopRequested` are used identically across tasks and tests. `StopCommand` matches the generated command from `[RelayCommand] private void Stop()`.
