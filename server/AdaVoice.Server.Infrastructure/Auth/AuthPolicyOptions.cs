namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Auth policy knobs, bound from the <c>Auth</c> configuration section in the Api and
/// registered as a singleton. Kept as a plain POCO (no <c>Microsoft.Extensions.Options</c>
/// dependency) so Infrastructure stays free of ASP.NET types. Defaults match the canonical
/// values in security-design.md §8 and the brief.</summary>
public sealed class AuthPolicyOptions
{
    /// <summary>Failed logins that trigger a lockout (security-design.md §8: 10).</summary>
    public int MaxFailedLogins { get; set; } = 10;

    /// <summary>Lockout duration in minutes (§8: 15).</summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Refresh-token sliding lifetime in days (brief: 30).</summary>
    public int RefreshSlidingDays { get; set; } = 30;

    /// <summary>Refresh-token absolute lifetime in days (brief: 90).</summary>
    public int RefreshAbsoluteDays { get; set; } = 90;
}
