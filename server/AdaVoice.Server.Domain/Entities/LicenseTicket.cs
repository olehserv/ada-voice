using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>One issued JWS ticket for a device activation. Primary key is <see cref="Jti"/>
/// (the ticket's jti claim), not Id. See docs/monetize/database-design.md §2 "license_tickets".</summary>
public class LicenseTicket
{
    public Guid Jti { get; set; }
    public Guid DeviceActivationId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset GraceUntil { get; set; }
    public LicenseTicketStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
