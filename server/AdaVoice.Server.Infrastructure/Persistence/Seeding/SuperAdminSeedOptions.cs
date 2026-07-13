namespace AdaVoice.Server.Infrastructure.Persistence.Seeding;

/// <summary>Credentials for the seeded super_admin account. There is deliberately no default
/// password anywhere in this type (§14 pitfall #19) — both values must come from the caller,
/// normally read from environment variables via <see cref="FromEnvironment(IReadOnlyDictionary{string,string?})"/>.</summary>
public sealed class SuperAdminSeedOptions
{
    public required string Email { get; init; }
    public required string Password { get; init; }

    /// <summary>Reads <c>ADAVOICE_SEED_SUPERADMIN_EMAIL</c> / <c>ADAVOICE_SEED_SUPERADMIN_PASSWORD</c>
    /// from the given environment map. Returns null (never throws) when either key is missing or
    /// whitespace, so callers can treat "not configured" as a normal, expected state — e.g. a local
    /// dev machine that only wants tenant/plan seed data. Takes the map as a parameter (rather than
    /// reading <see cref="Environment"/> directly) so this is unit-testable without mutating
    /// process-wide environment variables.</summary>
    public static SuperAdminSeedOptions? FromEnvironment(IReadOnlyDictionary<string, string?> env)
    {
        env.TryGetValue("ADAVOICE_SEED_SUPERADMIN_EMAIL", out var email);
        env.TryGetValue("ADAVOICE_SEED_SUPERADMIN_PASSWORD", out var password);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new SuperAdminSeedOptions { Email = email, Password = password };
    }

    /// <summary>Convenience overload reading the real process environment. Not unit-tested
    /// directly; <see cref="FromEnvironment(IReadOnlyDictionary{string,string?})"/> is.</summary>
    public static SuperAdminSeedOptions? FromEnvironment()
    {
        var map = new Dictionary<string, string?>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            map[(string)entry.Key] = (string?)entry.Value;
        }

        return FromEnvironment(map);
    }
}
