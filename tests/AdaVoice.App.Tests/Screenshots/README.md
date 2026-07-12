# Screenshot tests

`WindowScreenshotTests` renders every WPF window and setup-wizard step with a fake-backed
view-model and saves it as a PNG. Use these to see the actual UI without running the app by
hand — useful for design review and for checking a UI change didn't break another screen.

They drive real windows on screen (via FlaUI/UI Automation), so they need an interactive
desktop session and are skipped by default and excluded from headless CI.

## Run them

```powershell
$env:ADAVOICE_SCREENSHOTS = "1"
dotnet test tests/AdaVoice.App.Tests --filter "FullyQualifiedName~WindowScreenshotTests"
```

Renders the dark theme (the app's default). To render the light theme instead:

```powershell
$env:ADAVOICE_SCREENSHOTS = "1"
$env:ADAVOICE_SCREENSHOT_THEME = "Light"
dotnet test tests/AdaVoice.App.Tests --filter "FullyQualifiedName~WindowScreenshotTests"
```

## Where the screenshots land

- `docs/ui/screenshots/after/` — dark theme (default)
- `docs/ui/screenshots/after-light/` — light theme

Each run overwrites the PNGs in place, so the folders always reflect the latest UI.

## How it works

- `WpfAppFixture` owns one real `Application` and one STA "UI thread" for the whole run, shared
  across all tests via an xUnit collection fixture. It never calls `Application.Run()`, so
  `App.OnStartup` (single-instance mutex, WASAPI, `EngineHost`) never fires.
- `ScreenshotHarness` builds a window on that UI thread, waits for `ContentRendered`, then
  captures it from the calling (xUnit) thread — capture must run off the UI thread so the same
  process's UI Automation doesn't deadlock.
- The harness re-applies the requested theme before building every window: closing a WPF-UI
  `FluentWindow` resets the app's theme back to the OS setting as a side effect, so a one-time
  apply at startup isn't enough.
