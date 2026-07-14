using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Infrastructure.Persistence;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Maps a <see cref="UserRole"/> to its canonical text value (e.g.
/// <c>SuperAdmin</c> → <c>super_admin</c>) for the JWT <c>role</c> claim. Reuses the single
/// <see cref="StatusConverters.UserRole"/> mapping so the claim value can never drift from the
/// stored column value.</summary>
public static class RoleClaimValue
{
    public static string For(UserRole role) =>
        (string)StatusConverters.UserRole.ConvertToProvider(role)!;
}
