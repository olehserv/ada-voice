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

        // Let the layout settle and the Fluent compositor paint before grabbing pixels.
        app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Thread.Sleep(400);

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
