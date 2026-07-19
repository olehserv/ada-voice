using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FlaUI.UIA3;

namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// Shows a WPF window on the shared UI thread, waits for it to render, then captures it to a PNG
/// from the calling (xunit) thread via FlaUI. See <see cref="WpfAppFixture"/> for why the capture
/// runs on a different thread than the one that owns the window.
/// </summary>
public sealed class ScreenshotHarness(WpfAppFixture app)
{
    /// <summary>
    /// Builds the window with <paramref name="build"/> (on the UI thread), shows it, and writes
    /// <c>{name}.png</c> into <c>docs/ui/screenshots/{group}</c>. Returns the file path.
    /// </summary>
    public string Capture(Func<Window> build, string name, string group = "after")
    {
        Window window = null!;
        var handle = IntPtr.Zero;
        using var rendered = new ManualResetEventSlim(false);

        app.Dispatcher.Invoke(() =>
        {
            // Closing the previous window resets WPF-UI's ApplicationThemeManager back to the OS
            // theme as a side effect, so re-apply the requested theme before every window, not just
            // once at fixture startup (see WpfAppFixture.Theme).
            App.ApplyTheme(app.Theme);

            window = build();
            // Keep it on-screen and unobstructed — the capture reads real screen pixels.
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Topmost = true;
            window.ShowActivated = true;
            window.ContentRendered += (_, _) => rendered.Set();
            window.Show();
            window.Activate();
            handle = new WindowInteropHelper(window).Handle;
        });

        if (!rendered.Wait(TimeSpan.FromSeconds(15)))
            throw new TimeoutException($"'{name}' never raised ContentRendered.");

        // Let the layout settle and the Fluent compositor paint before grabbing pixels. 700 ms, not
        // 400 — Phase D's backdrop crossfade (Motion.State, 500 ms) fires its EnterActions as soon as
        // the state DataTrigger evaluates true (which happens before ContentRendered, on the initial
        // style application), so a shorter wait could capture mid-fade instead of settled.
        // Looping Storyboards (breathe/blink, Phase D steps 2/4) can't be made deterministic this way —
        // a resource-swap "run once for tests" override was tried and doesn't work (WPF can't freeze a
        // DynamicResource-valued timeline property inside a sealed Style.Trigger; see Controls.xaml's
        // backdrop-crossfade comment for the full story). Those loops are verified differently: a
        // stop/revert-after-exit check (does Opacity return to its rest value once the trigger exits,
        // not stay stuck mid-loop — see BackdropCrossfadeTests for the one-shot version of this idea),
        // not a screenshot.
        app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Thread.Sleep(700);

        var dir = TestPaths.ScreenshotDirectory(group);
        var path = Path.Combine(dir, name + ".png");

        using (var automation = new UIA3Automation())
        {
            var element = automation.FromHandle(handle);
            using var image = FlaUI.Core.Capturing.Capture.Element(element);
            image.ToFile(path);
        }

        app.Dispatcher.Invoke(window.Close);
        return path;
    }
}
