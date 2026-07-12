namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// A <see cref="FactAttribute"/> that skips unless <c>ADAVOICE_SCREENSHOTS=1</c>. The screenshot
/// tests drive real windows on screen, so they need an interactive desktop — a headless CI run of
/// <c>dotnet test</c> would time out waiting for a window to render. So they stay skipped in the
/// default suite and are run deliberately:
/// <code>ADAVOICE_SCREENSHOTS=1 dotnet test --filter Category=Screenshot</code>
/// (PowerShell: <c>$env:ADAVOICE_SCREENSHOTS=1; dotnet test --filter Category=Screenshot</c>).
/// </summary>
public sealed class ScreenshotFactAttribute : FactAttribute
{
    public ScreenshotFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ADAVOICE_SCREENSHOTS") != "1")
            Skip = "Set ADAVOICE_SCREENSHOTS=1 to render window screenshots (needs an interactive desktop).";
    }
}
