# Setup Wizard UI — Follow-Up Slice: VB-CABLE Link + Countdown Ring

## Context

The setup wizard (9-task plan, `docs/superpowers/plans/2026-07-02-setup-wizard-ui.md`) is fully
built, task-reviewed, and whole-branch-reviewed on branch `setup-wizard-ui` (worktree
`.claude/worktrees/setup-wizard-ui`, HEAD `9e0a0c9`). While closing out that review, I found the
implementation plan had silently dropped two things the original design spec
(`docs/superpowers/specs/2026-07-02-setup-wizard-ui-design.md`) called for:

1. A hyperlink to the VB-CABLE download page, shown when the cable environment check fails.
2. A purely cosmetic 5-second countdown-ring animation on the calibration step, owned entirely by
   the View (the spec is explicit the view-model must not track seconds).

Both are additive UI polish on top of the already-shipped, already-tested wizard. Neither requires
touching a view-model, a domain type, or the `ISetupHost` seam. The user decided to add both back
now rather than defer to v2. This plan continues on the same branch/worktree as the original
9-task plan — no new worktree needed.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task, continuing in
> the existing worktree `.claude/worktrees/setup-wizard-ui` (branch `setup-wizard-ui`, currently at
> HEAD `9e0a0c9`). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the two dropped spec items: (1) a VB-CABLE download link shown when the cable
environment check fails, and (2) a purely cosmetic 5-second countdown-ring animation on the
calibration step, owned entirely by the View.

**Architecture:** Task 1 (VB-CABLE link) adds one converter class to the existing
`Converters.cs`/`App.xaml` registration pattern, one `Hyperlink` in
`EnvironmentChecksStepView.xaml`, and one `RequestNavigate` handler in that view's code-behind —
no ViewModel, no new seam, matching the "OS action belongs in code-behind, not the ViewModel" rule
already implicit in this codebase's WPF/ViewModel split. Task 2 (countdown ring) adds two
concentric `Ellipse` elements plus a `Style.Triggers`-driven `Storyboard` to
`CalibrationStepView.xaml` only — pure XAML, no code-behind, no `DispatcherTimer`, matching the
spec's explicit "the View owns it; the view-model does not track seconds."

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), CommunityToolkit.Mvvm, WPF-UI 4.3.0, xUnit.

## Global Constraints (carried forward from the original plan, still binding)

- Spec: `docs/superpowers/specs/2026-07-02-setup-wizard-ui-design.md` — both tasks below implement
  a piece of it that the prior plan dropped.
- Follow the existing flat file layout: no new subfolders under `src/AdaVoice.App/` or
  `tests/AdaVoice.App.Tests/`.
- Run the full suite (`dotnet test --nologo` from the repo root) after every task; all prior tests
  must stay green.
- No unrelated scope creep: do not add device selection, live meters, the loopback self-test, the
  3 extra environment checks, or a `CheckType` enum to identify checks — those remain out of scope
  per the design spec's "Bucket A" boundary.
- **Binding values every task must use verbatim** (taken from the actual current source, not
  guessed — do not re-derive or "improve" these):
  - VB-CABLE download URL: `https://vb-audio.com/Cable/`
  - The cable check's `Name` string, exactly as produced by `EnvironmentChecks.cs`:
    `"Cable output"`.
  - Ring geometry (Task 2), for a 44px-diameter / 4px-stroke-thickness ring: radius = `20`,
    circumference = `125.664`, `StrokeDashArray` value (thickness-relative units) = `31.416`, and
    the `DoubleAnimation` `To` value (same thickness-relative units as `StrokeDashArray` — **not**
    the raw circumference) = `31.416`. These four numbers must match across the XAML exactly as
    given; do not recompute with different rounding.
- **Risk flagged, not resolved here — accepted trade-off:** `Name == "Cable output"` string
  matching is fragile — if `EnvironmentChecks.cs` ever renames that check, the link silently stops
  appearing with no compile error. No `CheckType` enum exists today, and adding one purely to make
  this link robust would ripple into the domain layer for a cosmetic feature — out of scope for
  this slice. Accepted as "good enough for now."

---

### Task 1: VB-CABLE download link on failed cable check

**Files:**
- Modify: `src/AdaVoice.App/Converters.cs`
- Modify: `src/AdaVoice.App/App.xaml`
- Modify: `src/AdaVoice.App/EnvironmentChecksStepView.xaml`
- Modify: `src/AdaVoice.App/EnvironmentChecksStepView.xaml.cs`
- Create: `tests/AdaVoice.App.Tests/FailedCableCheckToVisibilityConverterTests.cs`

**Interfaces:**
- Consumes: `AdaVoice.Audio.Setup.EnvironmentCheck`, `AdaVoice.Audio.Setup.CheckStatus` (already
  existing domain types; no changes). `Converters.cs` already has `using AdaVoice.Audio.Setup;`.
- Produces: `FailedCableCheckToVisibilityConverter : IValueConverter` (new, in `Converters.cs`),
  registered in `App.xaml` as `{StaticResource FailedCableCheckToLink}`. Consumed only by
  `EnvironmentChecksStepView.xaml`'s per-check `DataTemplate`. No ViewModel or domain change — the
  wizard's `EnvironmentChecksStepViewModel` is untouched.

The test project (`tests/AdaVoice.App.Tests/AdaVoice.App.Tests.csproj`) targets `net10.0-windows`
with `<UseWPF>true</UseWPF>` specifically so WPF types resolve in tests — so
`IValueConverter`/`Visibility` can be exercised directly here, same as any other C# type.

- [ ] **Step 1: Write the failing converter test**

Create `tests/AdaVoice.App.Tests/FailedCableCheckToVisibilityConverterTests.cs`:

```csharp
using System.Globalization;
using System.Windows;
using AdaVoice.App;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class FailedCableCheckToVisibilityConverterTests
{
    private static readonly FailedCableCheckToVisibilityConverter Sut = new();

    [Fact]
    public void Visible_when_the_cable_check_failed()
    {
        var check = new EnvironmentCheck("Cable output", CheckStatus.Fail, "not found");

        var result = Sut.Convert(check, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Collapsed_when_the_cable_check_passed()
    {
        var check = new EnvironmentCheck("Cable output", CheckStatus.Pass, "ok");

        var result = Sut.Convert(check, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Collapsed_when_a_different_check_fails()
    {
        var check = new EnvironmentCheck("Default output", CheckStatus.Fail, "is the cable");

        var result = Sut.Convert(check, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ConvertBack_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() =>
            Sut.ConvertBack(Visibility.Visible, typeof(EnvironmentCheck), null, CultureInfo.InvariantCulture));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter FailedCableCheckToVisibilityConverterTests`
Expected: FAIL — `FailedCableCheckToVisibilityConverter` does not exist.

- [ ] **Step 3: Add the converter**

In `src/AdaVoice.App/Converters.cs`, add at the end of the file (after `CheckStatusToBrushConverter`'s
closing brace):

```csharp

/// <summary>The whole bound <see cref="EnvironmentCheck"/> (via a path-less `{Binding}`) →
/// visible only for the failed cable-output check, so the VB-CABLE download link shows next to
/// that one check's detail text and nowhere else. Matches by <see cref="EnvironmentCheck.Name"/>
/// since no `CheckType` enum exists — see EnvironmentChecks.cs's "Cable output" literal.</summary>
public sealed class FailedCableCheckToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is EnvironmentCheck { Name: "Cable output", Status: CheckStatus.Fail }
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

This requires `using System.Windows;` for `Visibility` — `Converters.cs` currently has
`using System.Windows.Data;` and `using System.Windows.Media;` but not the bare `System.Windows`
namespace. Add it to the usings block at the top of the file:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AdaVoice.Audio.Setup;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter FailedCableCheckToVisibilityConverterTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Register the converter in `App.xaml`**

In `src/AdaVoice.App/App.xaml`, add after the `CheckStatusToBrush` registration:

```xml
            <app:CheckStatusToLabelConverter x:Key="CheckStatusToLabel" />
            <app:CheckStatusToBrushConverter x:Key="CheckStatusToBrush" />
            <app:FailedCableCheckToVisibilityConverter x:Key="FailedCableCheckToLink" />
```

(Only the new third line is added; the two existing lines are shown for exact insertion context.)

- [ ] **Step 6: Add the hyperlink to the view**

In `src/AdaVoice.App/EnvironmentChecksStepView.xaml`, the per-check `DataTemplate` currently reads:

```xml
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
```

Replace it with (only the new `TextBlock` after the `Detail` line is added):

```xml
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
                            <TextBlock Margin="0,4,0,0"
                                       Visibility="{Binding Converter={StaticResource FailedCableCheckToLink}}">
                                <Hyperlink NavigateUri="https://vb-audio.com/Cable/" RequestNavigate="OnVbCableLinkRequestNavigate">
                                    Download VB-CABLE
                                </Hyperlink>
                            </TextBlock>
                        </StackPanel>
                    </Border>
                </DataTemplate>
```

Note the `Visibility` binding uses a path-less `{Binding}` — the `Converter` receives the whole
`EnvironmentCheck` item (the `DataTemplate`'s `DataContext`), not one of its properties, exactly
like `{Binding Checks}`'s items already flow into this `DataTemplate` today.

- [ ] **Step 7: Add the `RequestNavigate` handler in code-behind**

`src/AdaVoice.App/EnvironmentChecksStepView.xaml.cs` currently reads:

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

Replace it with:

```csharp
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace AdaVoice.App;

/// <summary>The environment-checks step's view. Its <c>DataContext</c> is an
/// <c>EnvironmentChecksStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class EnvironmentChecksStepView : UserControl
{
    public EnvironmentChecksStepView() => InitializeComponent();

    /// <summary>Opens the VB-CABLE download link in the operator's default browser. A pure OS
    /// action with nothing to unit-test, so it lives here rather than in the ViewModel or a new
    /// host seam — there is no business logic to isolate, just a single link click.</summary>
    private void OnVbCableLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
```

- [ ] **Step 8: Build to verify it compiles clean**

Run: `dotnet build src/AdaVoice.App --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 9: Run the full App test suite to check for regressions**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, all tests (prior tests + the 4 new converter tests).

- [ ] **Step 10: Run the whole solution's test suite**

Run: `dotnet test --nologo`
Expected: PASS — every project green, no regressions.

- [ ] **Step 11: Manual smoke (WPF rendering is not unit-tested here, per the established pattern)**

1. Run: `dotnet run --project src/AdaVoice.App`
2. Open the setup wizard (first run, or click **Setup…** in the status bar).
3. If the cable check fails (VB-CABLE not installed), confirm a "Download VB-CABLE" link appears
   under that check's detail text, and clicking it opens `https://vb-audio.com/Cable/` in the
   default browser.
4. If the cable check passes, confirm no link appears on it or on any other check.

- [ ] **Step 12: Commit**

```bash
git add src/AdaVoice.App/Converters.cs src/AdaVoice.App/App.xaml src/AdaVoice.App/EnvironmentChecksStepView.xaml src/AdaVoice.App/EnvironmentChecksStepView.xaml.cs tests/AdaVoice.App.Tests/FailedCableCheckToVisibilityConverterTests.cs
git commit -m "feat(app): VB-CABLE download link on a failed cable-output check"
```

---

### Task 2: Cosmetic countdown-ring animation during calibration recording

**Files:**
- Modify: `src/AdaVoice.App/CalibrationStepView.xaml`

**Interfaces:**
- Consumes: `CalibrationStepViewModel.IsRecording` (already existing `[ObservableProperty]` bool;
  unchanged, not touched by this task).
- Produces: no new public interface — purely a visual addition inside the existing view. No
  code-behind change, no ViewModel change, matching the spec's "the View owns it; the view-model
  does not track seconds."

There are no unit tests in this task — WPF `Storyboard`/animation rendering is not unit-testable
in this codebase (same as every prior view file). Verification is build-clean + manual smoke.

**Ring geometry, computed and fixed (do not recompute):** diameter `44`, stroke thickness `4`,
radius `(44-4)/2 = 20`, circumference `2 × π × 20 = 125.664`, `StrokeDashArray` value in
thickness-relative units `125.664 / 4 = 31.416`, animation `To` in those same thickness-relative
units `31.416` (not `125.664` — `StrokeDashOffset` is expressed in the same
`StrokeThickness`-relative units as `StrokeDashArray`, per WPF's dash-pattern semantics).

- [ ] **Step 1: Add the ring above the "Recording… speak now" text**

`src/AdaVoice.App/CalibrationStepView.xaml` currently reads:

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

Insert this new `Grid` block between the `Start` button and the `Recording… speak now`
`TextBlock` (i.e. immediately before that `TextBlock` element, leaving every other existing
element untouched):

```xml
        <Grid Width="44" Height="44" Margin="0,0,0,8" HorizontalAlignment="Left"
              Visibility="{Binding IsRecording, Converter={StaticResource BoolToVis}}">
            <!-- Static background track. -->
            <Ellipse Width="44" Height="44" Stroke="{StaticResource Text.Secondary}" StrokeThickness="4" />

            <!-- Foreground ring: one dash and one gap, each equal to the full circumference (in
                 StrokeThickness-relative units: 125.664 / 4 = 31.416), rotated -90 degrees so the
                 drain starts at 12 o'clock. StrokeDashOffset animates 0 -> 31.416 (same
                 thickness-relative units as StrokeDashArray) over exactly 5 seconds while
                 IsRecording is true. -->
            <Ellipse x:Name="CountdownRing" Width="44" Height="44" Stroke="{StaticResource Accent}"
                     StrokeThickness="4" StrokeDashArray="31.416,31.416" StrokeDashOffset="0"
                     RenderTransformOrigin="0.5,0.5">
                <Ellipse.RenderTransform>
                    <RotateTransform Angle="-90" />
                </Ellipse.RenderTransform>
                <Ellipse.Style>
                    <Style TargetType="Ellipse">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsRecording}" Value="True">
                                <DataTrigger.EnterActions>
                                    <BeginStoryboard x:Name="CountdownBeginStoryboard">
                                        <Storyboard>
                                            <DoubleAnimation Storyboard.TargetProperty="StrokeDashOffset"
                                                             From="0" To="31.416" Duration="0:0:5" />
                                        </Storyboard>
                                    </BeginStoryboard>
                                </DataTrigger.EnterActions>
                                <DataTrigger.ExitActions>
                                    <StopStoryboard BeginStoryboardName="CountdownBeginStoryboard" />
                                </DataTrigger.ExitActions>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Ellipse.Style>
            </Ellipse>
        </Grid>
```

Notes on why this is correct WPF, not just plausible-looking XAML:
- `DataTrigger.ExitActions`'s `StopStoryboard` resets the animated property back to its base value
  (`StrokeDashOffset="0"` on the `Ellipse` itself) when `IsRecording` flips back to `false` — so a
  retry (`IsRecording` false→true again after a "too quiet" result) restarts the ring cleanly from
  a full ring each time, with no code-behind.
- The whole `Grid`'s `Visibility` is also bound to `IsRecording` (not just the ring's own
  opacity/visibility) so the ring and its `DataTrigger` are torn down and rebuilt with the rest of
  the recording-only UI — consistent with how `Succeeded`/`HasMessage` blocks are already toggled
  in this same file.
- `StopStoryboard` refers to the `BeginStoryboard` by its `x:Name`
  (`BeginStoryboardName="CountdownBeginStoryboard"`) — required for `StopStoryboard` to find it.
- `RenderTransform` has no XAML string shorthand ("Rotate -90" is invalid) — it must be the
  explicit `<Ellipse.RenderTransform><RotateTransform Angle="-90" /></Ellipse.RenderTransform>`
  element form shown above.

- [ ] **Step 2: Build to verify it compiles clean**

Run: `dotnet build src/AdaVoice.App --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Run the full App test suite to check for regressions**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, all tests, unchanged count from the end of Task 1 (this task adds no tests — pure
WPF animation, not unit-testable here).

- [ ] **Step 4: Run the whole solution's test suite**

Run: `dotnet test --nologo`
Expected: PASS — every project green, no regressions.

- [ ] **Step 5: Manual smoke (WPF animation rendering is not unit-tested here, per the established pattern)**

1. Run: `dotnet run --project src/AdaVoice.App`
2. Open the setup wizard, advance to the Calibration step, click **Start**.
3. Confirm a ring appears above "Recording… speak now" and visibly drains clockwise from full over
   about 5 seconds while recording, then the ring and text disappear together when recording ends.
4. If the result is "too quiet," click **Try again** and confirm the ring restarts from a full
   ring rather than continuing from wherever it stopped.

Exact visual tuning (animation direction, colors, size) is low-risk cosmetic polish — this smoke
check only needs to confirm nothing is broken or stuck, not pixel-perfect timing.

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.App/CalibrationStepView.xaml
git commit -m "feat(app): cosmetic countdown-ring animation during calibration recording"
```

---

## Verification (end-to-end)

1. After both tasks: `dotnet test --nologo` from the repo root — every project green (was 275
   passing before this plan; expect 279 after Task 1's 4 new converter tests, unchanged by Task 2).
2. Manual smoke per each task's Step 11/Step 5 above — these fold into the same outstanding manual
   GUI smoke pass already needed to close out the original 9-task wizard plan (first-run walkthrough
   with a real microphone), so do them together in one sitting once VB-CABLE is or isn't installed
   on the test machine.
3. Once both are smoke-tested, this — together with the original 9-task plan — is ready for
   `finishing-a-development-branch`.

## Self-review notes

- **Spec coverage:** both dropped spec items — the VB-CABLE link ("Scope > In this slice
  (Bucket A)") and the View-owned countdown ring ("Components > CalibrationStepViewModel") — now
  have a task each; no other spec item is touched or re-opened.
- **No ViewModel/domain/host changes:** `EnvironmentChecksStepViewModel`, `CalibrationStepViewModel`,
  `EnvironmentCheck`, `CheckStatus`, and `ISetupHost` are all read-only inputs to this plan, never
  modified — verified against the actual current file contents before writing this plan.
- **Binding-value consistency:** the VB-CABLE URL and the `"Cable output"` match string appear
  exactly once each (Task 1 Step 3 and Step 6), so there is no risk of two tasks drifting on the
  same literal. The four ring-geometry numbers in Task 2 are stated once in Global Constraints and
  once in Task 2's own preamble, both identical, then used verbatim in the one XAML block that
  needs them.
- **Known risk called out, accepted as a trade-off:** the `Name == "Cable output"` string match is
  fragile to a future rename in `EnvironmentChecks.cs`; no `CheckType` enum exists, and adding one
  is out of scope for this slice.
- **WPF-specific facts verified before finalizing this plan** (not assumed): confirmed
  `tests/AdaVoice.App.Tests.csproj` targets `net10.0-windows` with `<UseWPF>true</UseWPF>`, so the
  new converter is directly unit-testable; confirmed `RenderTransform` requires the explicit
  element form (no string shorthand); confirmed `StrokeDashOffset` is expressed in the same
  `StrokeThickness`-relative units as `StrokeDashArray` (not raw device-independent units), so the
  animation's `To` value equals the `StrokeDashArray` dash-length value (`31.416`), not the raw
  circumference (`125.664`) — a naive reading of the spec's animation would have gotten this wrong.
