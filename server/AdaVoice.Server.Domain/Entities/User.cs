using AdaVoice.Server.Domain.Abstractions;
using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>A user belonging to one tenant. Super admins live in a system tenant.
/// See docs/monetize/database-design.md §2 "users".</summary>
public class User : IHasTimestamps, IHasTenant
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; }
    public string? DisplayName { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
