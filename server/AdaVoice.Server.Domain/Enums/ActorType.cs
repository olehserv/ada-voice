namespace AdaVoice.Server.Domain.Enums;

/// <summary>Kind of actor that performed an audited action. Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum ActorType
{
    User,
    System,
    Admin,
}
