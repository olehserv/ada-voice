namespace AdaVoice.Server.Domain.Enums;

/// <summary>Role of a user within their tenant. Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum UserRole
{
    Operator,
    TenantAdmin,
    SuperAdmin,
}
