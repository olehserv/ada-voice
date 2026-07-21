using System.Globalization;
using System.Runtime.CompilerServices;

namespace AdaVoice.App.Tests;

/// <summary>
/// Pins every thread this test assembly creates to English by default. Without this, a machine
/// whose real OS/ambient UI culture is Ukrainian or Polish (plausible on a developer's own box)
/// makes verbatim-English string assertions fail for a reason that looks unrelated to the test
/// itself. <see cref="CultureInfo.DefaultThreadCurrentCulture"/>/
/// <see cref="CultureInfo.DefaultThreadCurrentUICulture"/> (not the instance <c>Current*</c>
/// setters) so it applies to threads created after this runs, not just whichever thread happens
/// to load the assembly first. A module initializer runs once, before any test collection or
/// fixture construction, so it is guaranteed to win that race.
/// </summary>
internal static class TestCultureInitializer
{
    [ModuleInitializer]
    public static void Run()
    {
        var english = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = english;
        CultureInfo.DefaultThreadCurrentUICulture = english;
    }
}
