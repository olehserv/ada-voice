using Microsoft.EntityFrameworkCore;

namespace AdaVoice.Server.Tests.Persistence;

// DB-less guard: builds the model against a dummy connection and compares it to the
// migration snapshot. No PostgreSQL is contacted, so NO [Trait("Category","Integration")].
// If this fails, the migration is stale relative to the model — regenerate it with
// `dotnet ef migrations add`, do NOT edit the test.
public class MigrationGuardTests
{
    [Fact]
    public void Migration_matches_the_current_model()
    {
        using var context = TestContext.Create();
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
