namespace AdaVoice.Server.Domain.Abstractions;

/// <summary>Marks an entity that records when its row was created. Plain C# — no EF
/// dependency — so Domain stays persistence-ignorant. The Infrastructure SaveChanges
/// interceptor stamps <see cref="CreatedAt"/> once, in one shared place.</summary>
public interface IHasCreatedAt
{
    DateTimeOffset CreatedAt { get; set; }
}
