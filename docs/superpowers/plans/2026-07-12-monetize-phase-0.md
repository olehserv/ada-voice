# Monetization Phase 0 — Repository Preparation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to
> implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The repo can host the monetization server without disturbing the desktop app or its
green test suite — five empty-but-buildable `server/` projects, a dev Postgres 16 compose file,
and the recorded hosting decision.

**Architecture:** Add a top-level `server/` folder with five projects in a locked dependency
direction (`Api → Infrastructure → Domain`, `Workers → Infrastructure`). No domain code, no EF
Core, no packages — that is Phase 1+. The projects join `AdaVoice.slnx`, so the existing CI
(`dotnet build/test AdaVoice.slnx`) builds and tests them with no workflow change. A dev-only
Docker Postgres 16 gives local dev a database. The hosting/URL decision is recorded as deferred.

**Tech Stack:** .NET 10, `Microsoft.NET.Sdk` (libs) + `Microsoft.NET.Sdk.Web` (Api), xUnit,
Docker Compose + `postgres:16`.

## Global Constraints (verbatim, apply to every task)

- Existing tests stay green at every commit. **Baseline measured 2026-07-12:** 459 passed,
  16 skipped (Audio 98, Core 104, Wasapi 8, Host 8, App 241+16 skipped). Build 0 warnings.
- Commit directly on `main`, no worktree, one commit per task.
- **No new NuGet packages.** The test project reuses versions already in
  `Directory.Packages.props` (`coverlet.collector`, `Microsoft.NET.Test.Sdk`, `xunit`,
  `xunit.runner.visualstudio`). Package versions never go in `.csproj`.
- Server projects inherit the root `Directory.Build.props` (`TreatWarningsAsErrors=true`,
  `Nullable`, `ImplicitUsings`, `Deterministic`). Do **not** add a `server/Directory.Build.props`.
- Locked dependency direction: `Api → Infrastructure → Domain`; `Workers → Infrastructure`.
  Nothing else. Domain has zero project references.
- Build only what Phase 0 lists. No entities, no EF Core, no endpoints beyond an empty host,
  no `AdaVoice.Licensing`, no edits to Core/Audio/Host/App.
- Never hard-code real secrets. The compose password is a throwaway **local dev** credential
  with an env-var override, clearly labelled dev-only.
- CI config (`.github/workflows/ci.yml`) is intentionally **not** edited (see Task 1 note).

## Section 14 pitfalls tagged Phase 0

**None.** Every pitfall in `security-design.md` §14 is tagged Phase 1/2/3/4/5/6/7/8/9/12.
The one Phase-0 artifact with a security angle is the compose file's DB credential — handled as
a dev-only env-overridable default (forward hygiene, not a §14 item).

## Open-question gate

No open question lists Phase 0 in its "Blocks" column. OQ-01 names Phase 0 only to say *don't*
block on it. Acceptance requires the hosting decision recorded as made-or-deferred with owner +
date → **deferred, owner Oleh, 2026-07-12** (Task 3).

## File Structure

- `server/AdaVoice.Server.Domain/AdaVoice.Server.Domain.csproj` — bottom layer, no deps.
- `server/AdaVoice.Server.Infrastructure/AdaVoice.Server.Infrastructure.csproj` — refs Domain.
- `server/AdaVoice.Server.Api/AdaVoice.Server.Api.csproj` + `Program.cs` — web host, refs Infrastructure.
- `server/AdaVoice.Server.Workers/AdaVoice.Server.Workers.csproj` — refs Infrastructure.
- `server/AdaVoice.Server.Tests/AdaVoice.Server.Tests.csproj` + `Architecture/DependencyDirectionTests.cs`.
- `AdaVoice.slnx` — add the five projects under a new `/server/` folder.
- `docker-compose.yml` (repo root) — dev Postgres 16.
- `docs/monetize/open-questions.md`, `handoff.md` — the gate record + status.

---

### Task 1: Server scaffold + dependency-direction guard

The five projects are interdependent and must land as one buildable commit. The architecture
test defines the required project graph, so it is the failing-test-first driver.

**Files:**
- Create: `server/AdaVoice.Server.Tests/AdaVoice.Server.Tests.csproj`
- Create: `server/AdaVoice.Server.Tests/Architecture/DependencyDirectionTests.cs`
- Create: `server/AdaVoice.Server.Domain/AdaVoice.Server.Domain.csproj`
- Create: `server/AdaVoice.Server.Infrastructure/AdaVoice.Server.Infrastructure.csproj`
- Create: `server/AdaVoice.Server.Api/AdaVoice.Server.Api.csproj`
- Create: `server/AdaVoice.Server.Api/Program.cs`
- Create: `server/AdaVoice.Server.Workers/AdaVoice.Server.Workers.csproj`
- Modify: `AdaVoice.slnx` (add `/server/` folder with five projects)

**Interfaces:**
- Produces: five buildable server projects in the locked graph; a passing xUnit test class
  `AdaVoice.Server.Tests.Architecture.DependencyDirectionTests` that parses the server `.csproj`
  files and asserts the graph. No public API surface for later tasks to consume in Phase 0.

**CI note:** No `ci.yml` change. The existing job runs `dotnet build AdaVoice.slnx` and
`dotnet test AdaVoice.slnx`; once the five projects are in the solution, CI builds and tests
them on every PR. Step 9 verifies the server tests appear in that exact command's output.

- [ ] **Step 1: Create the test project file**

`server/AdaVoice.Server.Tests/AdaVoice.Server.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the failing test**

`server/AdaVoice.Server.Tests/Architecture/DependencyDirectionTests.cs`:

```csharp
using System.Xml.Linq;

namespace AdaVoice.Server.Tests.Architecture;

// The server dependency direction is a locked constraint of the monetization design
// (Api -> Infrastructure -> Domain; Workers -> Infrastructure). This guard parses the
// project files so a later phase cannot quietly add a forbidden reference (for example
// Domain depending on Infrastructure, or a lower layer depending on Api/Workers).
public class DependencyDirectionTests
{
    private static readonly string ServerDir = LocateServerDir();

    [Fact]
    public void Domain_has_no_project_references()
    {
        Assert.Empty(ProjectReferencesOf("AdaVoice.Server.Domain"));
    }

    [Fact]
    public void Infrastructure_references_only_Domain()
    {
        Assert.Equal(new[] { "AdaVoice.Server.Domain" }, ProjectReferencesOf("AdaVoice.Server.Infrastructure"));
    }

    [Fact]
    public void Api_references_Infrastructure()
    {
        Assert.Contains("AdaVoice.Server.Infrastructure", ProjectReferencesOf("AdaVoice.Server.Api"));
    }

    [Fact]
    public void Workers_references_Infrastructure()
    {
        Assert.Contains("AdaVoice.Server.Infrastructure", ProjectReferencesOf("AdaVoice.Server.Workers"));
    }

    [Theory]
    [InlineData("AdaVoice.Server.Domain")]
    [InlineData("AdaVoice.Server.Infrastructure")]
    public void Lower_layers_do_not_reference_Api_or_Workers(string project)
    {
        var refs = ProjectReferencesOf(project);
        Assert.DoesNotContain("AdaVoice.Server.Api", refs);
        Assert.DoesNotContain("AdaVoice.Server.Workers", refs);
    }

    private static IReadOnlyList<string> ProjectReferencesOf(string project)
    {
        var path = Path.Combine(ServerDir, project, project + ".csproj");
        var doc = XDocument.Load(path);
        return doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string LocateServerDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AdaVoice.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate repo root (AdaVoice.slnx) from {AppContext.BaseDirectory}.");
        }

        return Path.Combine(dir.FullName, "server");
    }
}
```

- [ ] **Step 3: Run the test — expect RED**

Run: `dotnet test server/AdaVoice.Server.Tests/AdaVoice.Server.Tests.csproj -c Release`
Expected: FAIL — `XDocument.Load` throws `FileNotFoundException` because the four other
server `.csproj` files do not exist yet.

- [ ] **Step 4: Create the Domain project**

`server/AdaVoice.Server.Domain/AdaVoice.Server.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Domain layer: entities + status values (added in Phase 1). Bottom of the server
         stack: Api -> Infrastructure -> Domain. No dependencies, ever. -->
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

</Project>
```

- [ ] **Step 5: Create the Infrastructure project**

`server/AdaVoice.Server.Infrastructure/AdaVoice.Server.Infrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Infrastructure: EF Core, crypto, email (added in later phases). Depends on Domain. -->
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\AdaVoice.Server.Domain\AdaVoice.Server.Domain.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 6: Create the Api project + minimal host**

`server/AdaVoice.Server.Api/AdaVoice.Server.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <!-- REST API + admin Razor Pages (later phases). Composition root of the server.
         Depends on Infrastructure (-> Domain). -->
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\AdaVoice.Server.Infrastructure\AdaVoice.Server.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

`server/AdaVoice.Server.Api/Program.cs`:

```csharp
// Phase 0 scaffold: the server builds and runs but exposes no endpoints yet.
// The real API surface (auth, licensing, billing, admin) arrives in Phases 2+.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
```

- [ ] **Step 7: Create the Workers project**

`server/AdaVoice.Server.Workers/AdaVoice.Server.Workers.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Background jobs. MVP: hosted inside the Api process (split out Later).
         Depends on Infrastructure. -->
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\AdaVoice.Server.Infrastructure\AdaVoice.Server.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 8: Add the five projects to the solution**

Edit `AdaVoice.slnx` — add a `/server/` folder after `/tests/`:

```xml
  <Folder Name="/server/">
    <Project Path="server/AdaVoice.Server.Domain/AdaVoice.Server.Domain.csproj" />
    <Project Path="server/AdaVoice.Server.Infrastructure/AdaVoice.Server.Infrastructure.csproj" />
    <Project Path="server/AdaVoice.Server.Api/AdaVoice.Server.Api.csproj" />
    <Project Path="server/AdaVoice.Server.Workers/AdaVoice.Server.Workers.csproj" />
    <Project Path="server/AdaVoice.Server.Tests/AdaVoice.Server.Tests.csproj" />
  </Folder>
```

- [ ] **Step 9: Build + full suite — expect GREEN, server tests present**

Run: `dotnet build AdaVoice.slnx -c Release`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

Run: `dotnet test AdaVoice.slnx -c Release --no-build`
Expected: all existing tests still pass (Audio 98, Core 104, Wasapi 8, Host 8, App 241 +16
skipped) **and** a new line for `AdaVoice.Server.Tests.dll` with the 6 architecture tests
passing (4 facts + 2 theory cases). This is the exact command CI runs — its passing proves
the CI acceptance criterion without editing `ci.yml`.

- [ ] **Step 10: Commit**

```bash
git add server AdaVoice.slnx
git commit -m "feat(server): scaffold monetization server projects (Phase 0)"
```

---

### Task 2: Dev-only PostgreSQL 16 via Docker Compose

**Files:**
- Create: `docker-compose.yml` (repo root)

**Interfaces:**
- Produces: `docker compose up -d` starts a `postgres:16` container reachable at
  `Host=localhost;Port=5432;Database=adavoice;Username=adavoice;Password=adavoice_dev`.

This task's "passing check" is a command, not a unit test (there is nothing to unit-test in a
compose file).

- [ ] **Step 1: Write the compose file**

`docker-compose.yml`:

```yaml
# AdaVoice — local development database (DEV ONLY, never production).
#
# Phase 0 of the monetization roadmap. Starts a PostgreSQL 16 instance for local server
# development. Production hosting, TLS, and backups are decided in Phase 11 (see
# docs/monetize/open-questions.md OQ-01/OQ-11).
#
# Usage:
#   docker compose up -d      # start Postgres 16 in the background
#   docker compose down       # stop it (data survives in the named volume)
#   docker compose down -v    # stop and wipe the data volume
#
# Connection string (EF Core / Npgsql), matching the defaults below:
#   Host=localhost;Port=5432;Database=adavoice;Username=adavoice;Password=adavoice_dev
#
# The published port is bound to 127.0.0.1 only, so the dev DB is not reachable from other hosts.
#
# The password below is a throwaway LOCAL DEV credential. It is not a secret and must never be
# reused in staging or production, which take real secrets from environment variables (see
# docs/monetize/security-design.md). Override any value locally via a .env file (git-ignored).
services:
  postgres:
    image: postgres:16
    container_name: adavoice-postgres
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-adavoice}
      POSTGRES_USER: ${POSTGRES_USER:-adavoice}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-adavoice_dev}
    ports:
      - "127.0.0.1:5432:5432"
    volumes:
      - adavoice-pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER:-adavoice} -d ${POSTGRES_DB:-adavoice}"]
      interval: 5s
      timeout: 5s
      retries: 5

volumes:
  adavoice-pgdata:
```

- [ ] **Step 2: Validate the compose file**

Run: `docker compose config -q`
Expected: no output, exit 0 (valid schema, interpolation resolves via defaults).

- [ ] **Step 3: Start Postgres and confirm it is version 16**

Run:
```bash
docker compose up -d
docker compose exec -T postgres bash -c 'until pg_isready -U adavoice -d adavoice; do sleep 1; done'
docker compose exec -T postgres psql -U adavoice -d adavoice -c "select version();"
```
Expected: `PostgreSQL 16.x ...` in the output.

- [ ] **Step 4: Tear down**

Run: `docker compose down`
Expected: container removed; the `adavoice-pgdata` volume remains.

- [ ] **Step 5: Commit**

```bash
git add docker-compose.yml
git commit -m "chore(server): add dev-only Postgres 16 docker-compose (Phase 0)"
```

---

### Task 3: Record the hosting-decision gate + update handoff

**Files:**
- Modify: `docs/monetize/open-questions.md` (section 9)
- Modify: `handoff.md`

Docs only; the "passing check" is a clean diff plus the already-green suite.

- [ ] **Step 1: Record the deferral in open-questions.md**

In `docs/monetize/open-questions.md` section 9 ("Resolved"), replace the empty table body with
two dated rows (newest first):

```markdown
| Date | ID | Decision (one line) |
|---|---|---|
| 2026-07-12 | OQ-02 | Phase 0 gate: domain / API base URL **deferred** with OQ-01. Client keeps the base URL replaceable. Owner: Oleh. Needed by Phase 6 (staging URL), final by Phase 11. |
| 2026-07-12 | OQ-01 | Phase 0 gate: hosting location/provider **deferred**. MVP Phases 1–10 run on local Docker Postgres 16 (`docker-compose.yml`). Owner: Oleh. Revisit at Phase 11 (production deploy). |
```

- [ ] **Step 2: Update handoff.md status**

In `handoff.md`, add a dated "Latest work" bullet for the Phase 0 scaffold, and correct the
"Next action" / "Monetization" notes: Phase 0 is done; Phase 1 (domain + DB) is next;
OQ-12/OC-06 is still needed before **Phase 4** (device activation), not before Phase 0/1.
Update the one-line test count to mention the server test project. Keep it compact.

- [ ] **Step 3: Confirm the suite is still green (nothing code changed, but prove it)**

Run: `dotnet test AdaVoice.slnx -c Release --no-build`
Expected: same green result as Task 1 Step 9.

- [ ] **Step 4: Commit**

```bash
git add docs/monetize/open-questions.md handoff.md
git commit -m "docs(monetize): record Phase 0 hosting-gate deferral and update handoff"
```

---

## Acceptance-criteria trace (Phase 0)

| Criterion | Proven by |
|---|---|
| `dotnet build` succeeds with all new projects; existing tests stay green | Task 1 Step 9 |
| CI runs server build + tests on every PR | Task 1 Step 9 (CI runs the identical `dotnet build/test AdaVoice.slnx`; server projects are in the solution) |
| `docker compose up` starts Postgres 16; connection string documented in compose comments | Task 2 Steps 2–3 + the comment block |
| Hosting decision made or explicitly deferred with named owner + date | Task 3 Step 1 (deferred, owner Oleh, 2026-07-12) |

## Self-review notes

- Spec coverage: all four acceptance criteria and all five roadmap tasks are covered (Task 5
  "open question gate" → Task 3; CI Task 3 → Task 1 note; scaffold Tasks 1–2 → Task 1; compose
  Task 4 → Task 2). No gaps.
- No placeholders: every file's full content is inline.
- Type consistency: the test helper names (`ProjectReferencesOf`, `LocateServerDir`) and
  project names are used identically across steps.
