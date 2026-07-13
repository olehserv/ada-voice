using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace AdaVoice.Server.Infrastructure.Persistence;

/// <summary>Generates time-ordered UUID v7 primary keys app-side. PostgreSQL 16 has no
/// native uuidv7, so keys are produced here whenever the caller has not supplied one.
/// Values are permanent (not temporary), so EF keeps the generated key as the real id.</summary>
public sealed class UuidV7ValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}
