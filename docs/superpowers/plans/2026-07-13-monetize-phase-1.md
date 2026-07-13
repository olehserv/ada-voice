# AdaVoice Monetization — Phase 1: Domain Model & Database — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. On execution, **first copy this plan to `docs/superpowers/plans/2026-07-13-monetize-phase-1.md` and commit it** (repo convention + persist-plans-in-repo).

## Context

AdaVoice is adding a monetization backend (ASP.NET Core + PostgreSQL 16 + EF Core) in the `server/` folder, built across roadmap Phases 0–12. **Phase 0 shipped** the empty-but-buildable `server/` scaffold (5 projects, dependency-direction guard, dev docker-compose PG16). **Phase 1** is the domain-model + database foundation: turn the canonical 13-table schema from `docs/monetize/database-design.md` into EF Core entities, a DbContext with migrations, multi-tenant query filters, and an idempotent seeder. No auth, no endpoints, no billing logic — those are Phases 2+.

**Goal:** All 13 canonical tables exist as EF Core entities with a migration, multi-tenant global query filters, and an idempotent seeder (default plans + one super_admin).

**Architecture:** Domain = persistence-ignorant POCOs + status enums (zero dependencies). Infrastructure = DbContext, per-entity `IEntityTypeConfiguration`, enum↔text value converters, snake_case, UUID v7 keys, timestamptz audit columns, `ITenantProvider` + global query filters, a SaveChanges interceptor that stamps timestamps and tenant_id in one shared place, the initial migration, and the seeder. Api gets only the EF `Design` package (design-time); its `Program.cs` stays inert this phase. Tests are real-Postgres integration tests (against the dev docker-compose PG16 / a CI PG16 service) plus DB-less model/guard tests.

**Tech Stack:** .NET 10, EF Core 10, Npgsql, PostgreSQL 16, xUnit. Central package management (`Directory.Packages.props`), `TreatWarningsAsErrors=true`.

## Global Constraints

Every task's requirements implicitly include this section. Copy binding items into each reviewer prompt.

- **Existing tests stay green at every commit.** The desktop app's behavior must NOT change. No new package/project reference on `AdaVoice.Core`, `AdaVoice.Audio`, `AdaVoice.Audio.Wasapi`, `AdaVoice.Host`, or `AdaVoice.App`.
- **Locked dependency direction:** `Api → Infrastructure → Domain`; `Workers → Infrastructure`. Domain has ZERO project references. The Phase-0 `DependencyDirectionTests` guard must stay green. (Test project referencing Infrastructure/Domain is allowed and not covered by the guard.)
- **Package versions go in `Directory.Packages.props` only**, never in `.csproj`. Approved Phase-1 additions (exact versions):
  - `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** → Infrastructure
  - `EFCore.NamingConventions` **10.0.1** → Infrastructure
  - `Microsoft.Extensions.Identity.Core` **10.0.9** → Infrastructure
  - `Microsoft.EntityFrameworkCore.Design` **10.0.9** → Api (`PrivateAssets=all`)
  - No test-only EF package: the Tests project gets Npgsql transitively via its Infrastructure project reference.
- **Canonical schema is `docs/monetize/database-design.md`** (source of truth for names, types, nullability, statuses). `README.md` (the brief) wins on any conflict. Deviating from a canonical decision needs a new ADR + owner approval.
- **Secrets from env vars only.** The seeded super_admin password comes from an env var with **no default** and is **never logged**. The dev DB connection string is the documented docker-compose throwaway (`Host=localhost;Port=5432;Database=adavoice;Username=adavoice;Password=adavoice_dev`) — dev-only, not a secret.
- **Build only what Phase 1 lists.** No auth, no endpoints, no billing/subscription state machine, no workers, no startup auto-migrate/seed wiring, no table partitioning, no soft-delete beyond `tenants.status='deleted'`, no `must_change_password` column.
- **Conventions (database-design.md §2, §4):** `uuid` PKs (v7), `created_at`/`updated_at timestamptz not null` on every table EXCEPT `audit_logs` and `idempotency_keys` (which have `created_at` only). Statuses stored as `text` + CHECK constraints (NOT native PG enums), mapped to C# enums via value converters. `license_tickets` PK is `jti` (not `id`).

## §14 Security-pitfall coverage (each becomes a test or explicit review point)

| Pitfall | Tag | How Phase 1 covers it |
|---|---|---|
| **#16** Global filters don't cover writes/raw SQL | Phase 1/3 | `tenant_id` is set in ONE shared place — the SaveChanges interceptor from `ITenantProvider`, never from a caller-supplied value. Integration test proves cross-tenant isolation with two context instances. Source-scan guard test forbids `FromSqlRaw`/`IgnoreQueryFilters` (Task 5). |
| **#18** String interpolation into raw SQL | Phase 1+ | Same source-scan guard forbids `FromSqlRaw(`; policy = `FromSqlInterpolated` only. No raw SQL in Phase 1, so guard passes at zero occurrences and blocks future regressions. |
| **#19** Seeded super_admin is the weakest door | Phase 1 | Password from env var, **no default**; seeder never logs it; hash produced by `PasswordHasher`; `last_login_at` seeded null so Phase 2 login can force a change. Tests assert all four. |
| **#17** Workers meet tenant filters expecting a request | Phase 8 (decide in Phase 1) | Recorded decision (below + code comment): workers will iterate tenants explicitly and set tenant context per batch, or use `IgnoreQueryFilters()` deliberately with a comment + audit row. The enforcing test lands in Phase 8. No worker code in Phase 1. |

## Phase-1 decisions (record in the committed plan; not schema/code beyond noted)

- **`audit_logs` is excluded from the global query filter.** Its `tenant_id` is nullable (system-wide rows), and super_admin needs cross-tenant reads. A blanket `TenantId == current` filter would silently hide system rows. Decision: no global filter on `audit_logs`; tenant scoping for audit reads is handled explicitly at query sites in later phases.
- **`Program.cs` stays inert; no startup migrate/seed wiring.** The roadmap Phase-1 tasks list "create the migration" and "write the seeder", not "run them at startup". AC3 tests the seeder directly; AC1 uses `dotnet ef database update`. A design-time factory lets `dotnet ef` work without a live host. (db-design §5 mentions startup seeding behind a flag — deferred to when the Api gains a real host.)
- **UUID v7 is generated app-side** via `Guid.CreateVersion7()` in a shared EF `ValueGenerator<Guid>` (PG16 has no native uuidv7). Provider-agnostic; works for keys not supplied by the caller.
- **No `must_change_password` column.** "Force change on first login" (Phase 2) keys off `last_login_at IS NULL`. Zero schema cost.

## File Structure

```text
server/AdaVoice.Server.Domain/
  Enums/                      # 10 status enums, one file each
  Entities/                   # 13 POCOs, one file each
server/AdaVoice.Server.Infrastructure/
  Persistence/
    AdaVoiceDbContext.cs
    AdaVoiceDbContextFactory.cs        # IDesignTimeDbContextFactory
    ITenantProvider.cs                 # + AmbientTenantProvider (settable)
    UuidV7ValueGenerator.cs
    AuditableTenantInterceptor.cs      # timestamps + tenant_id stamping
    Configurations/                    # one IEntityTypeConfiguration<T> per entity
    Seeding/
      DatabaseSeeder.cs
      SuperAdminSeedOptions.cs         # + FromEnvironment() reader (no default, no log)
  Migrations/                          # generated by `dotnet ef migrations add InitialCreate`
server/AdaVoice.Server.Tests/
  Domain/StatusEnumTests.cs
  Persistence/ModelShapeTests.cs               # DB-less
  Persistence/MigrationGuardTests.cs           # DB-less (HasPendingModelChanges)
  Persistence/PostgresFixture.cs               # [Collection] fixture: unique DB + Migrate + drop
  Persistence/SchemaIntegrationTests.cs        # [Integration] AC1: 13 tables, timestamptz
  Persistence/TenantIsolationTests.cs          # [Integration] AC2 + #16
  Persistence/SeederTests.cs                   # [Integration] AC3 + #19
  Architecture/RawSqlGuardTests.cs             # DB-less source-scan (#16/#18)
.github/workflows/ci.yml                       # + ubuntu server-tests job w/ PG16 service
```

Integration tests carry `[Trait("Category","Integration")]`. DB-less tests do not.

---

### Task 1: Domain entities and status enums

**Files:**
- Create: `server/AdaVoice.Server.Domain/Enums/*.cs` (10 files), `server/AdaVoice.Server.Domain/Entities/*.cs` (13 files)
- Modify: `server/AdaVoice.Server.Tests/AdaVoice.Server.Tests.csproj` (add `<ProjectReference>` to Domain)
- Test: `server/AdaVoice.Server.Tests/Domain/StatusEnumTests.cs`

**Interfaces — Produces:** 10 enums and 13 POCO entity types consumed by Task 2's configurations. Exact enum members (PascalCase C# name → canonical text value used by the Task-2 converter):

- `TenantStatus`: Active, Suspended, Cancelled, Deleted → `active/suspended/cancelled/deleted`
- `UserRole`: Operator, TenantAdmin, SuperAdmin → `operator/tenant_admin/super_admin`
- `UserStatus`: Active, Disabled → `active/disabled`
- `SubscriptionStatus`: Trial, Active, PastDue, GracePeriod, Suspended, Cancelled, Expired → `trial/active/past_due/grace_period/suspended/cancelled/expired`
- `DeviceStatus`: Active, Revoked, Blocked, Expired → `active/revoked/blocked/expired`
- `InvoiceStatus`: Draft, Issued, Paid, Overdue, Cancelled, Refunded → `draft/issued/paid/overdue/cancelled/refunded`
- `PaymentProvider`: ManualBankTransfer, LiqPay, WayForPay, Fondy → `manual_bank_transfer/liqpay/wayforpay/fondy`
- `LicenseTicketStatus`: Issued, Revoked → `issued/revoked`
- `SigningKeyStatus`: Active, Next, Retired → `active/next/retired`
- `ActorType`: User, System, Admin → `user/system/admin`

Entity property specs = the field tables in `docs/monetize/database-design.md §2`, verbatim. Types: `uuid`→`Guid`, `text`→`string` (nullable per the Null column), `timestamptz`→`DateTimeOffset` (nullable→`DateTimeOffset?`), `int`→`int`, `numeric(12,2)`→`decimal`, `boolean`→`bool`, `jsonb`→`string` (raw JSON; typed later if needed), `bytea`→`byte[]`. Status columns use the enum types above. `created_at`/`updated_at` are `DateTimeOffset`; `audit_logs`/`idempotency_keys` have only `CreatedAt`. `license_tickets` key property is `Jti` (Guid). POCOs hold scalar columns + FK id properties; navigation properties optional (add only where a config needs them — keep minimal). No EF attributes on entities (persistence-ignorant).

- [ ] **Step 1: Write the failing test** — `StatusEnumTests.cs`, one `[Fact]` per enum asserting exact member set. Example:

```csharp
using AdaVoice.Server.Domain.Enums;

public class StatusEnumTests
{
    [Fact]
    public void SubscriptionStatus_has_the_seven_canonical_members() =>
        Assert.Equal(
            new[] { "Trial", "Active", "PastDue", "GracePeriod", "Suspended", "Cancelled", "Expired" },
            Enum.GetNames<SubscriptionStatus>());

    [Fact]
    public void TenantStatus_has_the_four_canonical_members() =>
        Assert.Equal(new[] { "Active", "Suspended", "Cancelled", "Deleted" }, Enum.GetNames<TenantStatus>());
    // ... one fact per remaining enum (DeviceStatus, InvoiceStatus, PaymentProvider,
    //     UserRole, UserStatus, LicenseTicketStatus, SigningKeyStatus, ActorType)
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test server/AdaVoice.Server.Tests` → FAIL (types don't exist / won't compile).
- [ ] **Step 3: Implement** — create the 10 enum files and 13 entity POCO files per the specs above. Add the Domain `<ProjectReference>` to the test csproj.
- [ ] **Step 4: Run to verify it passes** — `dotnet test server/AdaVoice.Server.Tests` → PASS. Also `dotnet build AdaVoice.slnx` → 0 warnings/0 errors.
- [ ] **Step 5: Commit** — `feat(server): add Phase 1 domain entities and status enums`

---

### Task 2: EF Core DbContext, configurations, tenant filters, interceptor

**Files:**
- Modify: `Directory.Packages.props` (add the 4 approved `PackageVersion` entries); `server/AdaVoice.Server.Infrastructure/AdaVoice.Server.Infrastructure.csproj` (add 3 `PackageReference`s, no versions); `server/AdaVoice.Server.Api/AdaVoice.Server.Api.csproj` (add `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets=all`); `server/AdaVoice.Server.Tests/AdaVoice.Server.Tests.csproj` (add Infrastructure `<ProjectReference>`)
- Create: `Persistence/AdaVoiceDbContext.cs`, `ITenantProvider.cs`, `UuidV7ValueGenerator.cs`, `AuditableTenantInterceptor.cs`, `AdaVoiceDbContextFactory.cs`, `Persistence/Configurations/*.cs` (13)
- Test: `server/AdaVoice.Server.Tests/Persistence/ModelShapeTests.cs` (DB-less)

**Interfaces:**
- Consumes: the 13 entities + 10 enums from Task 1.
- Produces: `AdaVoiceDbContext(DbContextOptions, ITenantProvider)` with `DbSet<>` for all 13 entities; `ITenantProvider { Guid? CurrentTenantId { get; } }` + `AmbientTenantProvider` (settable `CurrentTenantId`); `AuditableTenantInterceptor`; `AdaVoiceDbContextFactory` (design-time). Task 3 consumes the context/factory for migration + fixtures.

Key configuration rules (in `OnModelCreating` + per-entity configs):
- `optionsBuilder.UseSnakeCaseNamingConvention()` — set where the context is built (factory + tests + interceptor registration).
- Every uuid PK uses `UuidV7ValueGenerator` (`HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd()`), returning `Guid.CreateVersion7()`.
- `license_tickets`: `HasKey(x => x.Jti)`.
- Each status property: `.HasConversion(new EnumToStringConverter-style converter mapping to the canonical text)` — use explicit `ValueConverter<TEnum,string>` with the exact text values from Task 1 (do NOT rely on default enum member names — `PastDue`→`past_due` etc.).
- CHECK constraints per enum column: `builder.ToTable(t => t.HasCheckConstraint("ck_<table>_<col>", "<col> IN ('a','b',...)"))` with the canonical text list.
- Column types: `DateTimeOffset` audit columns → timestamptz (Npgsql default for `DateTimeOffset`); `jsonb` columns (`plans.features`, `usage_events.data`, `audit_logs.data`, `idempotency_keys.response_body`) → `.HasColumnType("jsonb")`; `signing_keys.private_key_encrypted` → `bytea` (byte[] default); money → `.HasColumnType("numeric(12,2)")`.
- Indexes/uniques per `database-design.md §3`: `users` unique `(tenant_id, lower(email))` (`.HasIndex(...).IsUnique()` with `.HasFilter`/expression as needed); `subscriptions` partial unique `(tenant_id)` where `status not in ('cancelled','expired')` (`.HasFilter`); `refresh_tokens` unique `(token_hash)` + index `(family_id)`; `device_activations` unique `(tenant_id, device_id)` + index `(tenant_id, status)`; `invoices` unique `(number)` + index `(status, due_at)`; `payments` unique `(provider, provider_tx_id)` where not null (`.HasFilter("provider_tx_id IS NOT NULL")`); `license_tickets` index `(expires_at)` + `(device_activation_id, status)`; `audit_logs` index `(tenant_id, created_at)`; `usage_events` index `(tenant_id, occurred_at)`; `idempotency_keys` unique `(key, endpoint)` + index `(expires_at)`.
- **Global query filters** on `users`, `subscriptions`, `device_activations`, `invoices`, `usage_events`: `builder.HasQueryFilter(e => e.TenantId == _tenantProvider.CurrentTenantId)`. **NOT** on `audit_logs` (decision above), nor on tables without a `tenant_id` column.
- `audit_logs`: no `updated_at`; append-only (no special config needed beyond leaving `UpdatedAt` off the model). `idempotency_keys`: no `updated_at`.

`AuditableTenantInterceptor : SaveChangesInterceptor` — override `SavingChanges`/`SavingChangesAsync`: for `Added` entities stamp `CreatedAt`/`UpdatedAt`; for `Modified` stamp `UpdatedAt`; for `Added` tenant-owned entities whose `TenantId` is default, set it from `ITenantProvider.CurrentTenantId` (the single shared place — #16). Timestamps use `DateTimeOffset.UtcNow`.

`AdaVoiceDbContextFactory : IDesignTimeDbContextFactory<AdaVoiceDbContext>` — reads `ADAVOICE_DB_CONNECTION` env var, falls back to the documented docker-compose dev string; `UseNpgsql(conn, o => o.MigrationsAssembly("AdaVoice.Server.Infrastructure")).UseSnakeCaseNamingConvention()`; passes a system `ITenantProvider` (returns null).

- [ ] **Step 1: Write the failing test** — `ModelShapeTests.cs` (DB-less; builds the model against Npgsql with a dummy connection string, no connection opened):

```csharp
[Fact]
public void Tenant_owned_entities_have_a_global_query_filter()
{
    using var ctx = TestContext.Create();            // helper: UseNpgsql(dummy)+UseSnakeCaseNamingConvention
    foreach (var t in new[] { typeof(User), typeof(Subscription), typeof(DeviceActivation),
                              typeof(Invoice), typeof(UsageEvent) })
        Assert.NotNull(ctx.Model.FindEntityType(t)!.GetQueryFilter());
}

[Fact]
public void AuditLog_is_deliberately_not_filtered()
{
    using var ctx = TestContext.Create();
    Assert.Null(ctx.Model.FindEntityType(typeof(AuditLog))!.GetQueryFilter());
}

[Fact]
public void LicenseTicket_primary_key_is_jti()
{
    using var ctx = TestContext.Create();
    var pk = ctx.Model.FindEntityType(typeof(LicenseTicket))!.FindPrimaryKey()!;
    Assert.Equal("Jti", Assert.Single(pk.Properties).Name);
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL (context/types don't exist).
- [ ] **Step 3: Implement** — packages, csproj refs, context, provider, value generator, interceptor, factory, 13 configs, `TestContext` helper.
- [ ] **Step 4: Run to verify it passes** — `dotnet test server/AdaVoice.Server.Tests --filter Category!=Integration` → PASS. `dotnet build AdaVoice.slnx` → 0/0. Existing `DependencyDirectionTests` still green.
- [ ] **Step 5: Commit** — `feat(server): add EF Core DbContext, tenant filters, and audit/tenant interceptor`

---

### Task 3: Initial migration + schema and tenant-isolation integration tests

**Files:**
- Create (generated): `server/AdaVoice.Server.Infrastructure/Migrations/*_InitialCreate.cs` + `AdaVoiceDbContextModelSnapshot.cs`
- Create: `Persistence/PostgresFixture.cs`, `Persistence/SchemaIntegrationTests.cs`, `Persistence/TenantIsolationTests.cs`, `Persistence/MigrationGuardTests.cs`

**Interfaces — Produces:** `PostgresFixture` (an `ICollectionFixture`) that creates a uniquely-named database on the target PG server, runs `Migrate()`, exposes a factory for fresh `AdaVoiceDbContext` instances with a chosen `ITenantProvider`, and drops the database on dispose. Consumed by Tasks 4 tests.

- [ ] **Step 1: Add the migration** — run (`ADAVOICE_DB_CONNECTION` set, docker-compose up):
  `dotnet ef migrations add InitialCreate --project server/AdaVoice.Server.Infrastructure --startup-project server/AdaVoice.Server.Api`
- [ ] **Step 2: Write the failing tests:**
  - `MigrationGuardTests` (DB-less): `Assert.False(TestContext.Create().Database.HasPendingModelChanges());` — proves the migration matches the model.
  - `SchemaIntegrationTests` `[Trait("Category","Integration")]` (AC1): after fixture `Migrate()`, query `information_schema.tables` for all 13 snake_case names; query `information_schema.columns` asserting `created_at`/`updated_at` are `timestamp with time zone`.
  - `TenantIsolationTests` `[Trait("Category","Integration")]` (AC2 + #16):
    - Seed rows for tenant A and tenant B (via `IgnoreQueryFilters` or a system provider on insert).
    - A **fresh** context with provider=A returns only A's rows; a **separate fresh** context with provider=B returns only B's; a context using `IgnoreQueryFilters()` sees both. (Two distinct instances — never mutate one provider.)
    - #16: adding a `User` without setting `TenantId` under provider=A stamps it to A; it is invisible to a fresh provider=B context.
- [ ] **Step 3: Run to verify they fail** — migration guard passes only once the migration exists; integration tests fail until the fixture is implemented.
- [ ] **Step 4: Implement `PostgresFixture`; run to verify pass** — `dotnet test server/AdaVoice.Server.Tests` (docker-compose PG16 up) → all PASS, including `Category=Integration`.
- [ ] **Step 5: Verify AC1 by command** — `dotnet ef database update --project server/AdaVoice.Server.Infrastructure --startup-project server/AdaVoice.Server.Api` against docker-compose; confirm 13 tables (`\dt` or `information_schema`). Capture output for handoff.
- [ ] **Step 6: Commit** — `feat(server): add InitialCreate migration with schema and tenant-isolation tests`

---

### Task 4: Idempotent seeder (plans + super_admin)

**Files:**
- Create: `Persistence/Seeding/DatabaseSeeder.cs`, `Persistence/Seeding/SuperAdminSeedOptions.cs`
- Test: `server/AdaVoice.Server.Tests/Persistence/SeederTests.cs` `[Trait("Category","Integration")]`

**Interfaces — Produces:** `DatabaseSeeder(AdaVoiceDbContext, IPasswordHasher<User>, ILogger<DatabaseSeeder>)` with `Task SeedAsync(SuperAdminSeedOptions options, CancellationToken)`; `SuperAdminSeedOptions { string Email; string Password; }` + `static SuperAdminSeedOptions? FromEnvironment(IReadOnlyDictionary<string,string?> env)` reading `ADAVOICE_SEED_SUPERADMIN_EMAIL` / `ADAVOICE_SEED_SUPERADMIN_PASSWORD` — returns null (no throw) when either is absent; **no default password**.

Seeder behavior: create the system tenant if absent; create the default plan(s) (existence check by `code`) if absent; create the super_admin (existence check by email within system tenant) if absent, in the system tenant, `Role=SuperAdmin`, `LastLoginAt=null`, `PasswordHash = hasher.HashPassword(...)`. **Never** log the password (log only the email and a "created/exists" outcome). If options are null, log a warning ("super_admin seed skipped: env vars not set") and seed only plans/system tenant. Existence checks make re-runs safe.

- [ ] **Step 1: Write the failing tests:**
  - AC3 idempotency: `SeedAsync` twice → exactly one system tenant, the expected plan count, exactly one super_admin (no duplicates).
  - #19a: with env password set, the stored `PasswordHash` verifies via `hasher.VerifyHashedPassword(...)==Success` and is **not** equal to the plaintext.
  - #19b: `SuperAdminSeedOptions.FromEnvironment` returns null when the password var is missing (no default), and seeding then creates no super_admin.
  - #19c: password never logged — inject a capturing `ILogger`, seed, assert no logged message contains the plaintext password.
  - #19d: seeded super_admin has `LastLoginAt == null`.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement the seeder + options reader.**
- [ ] **Step 4: Run to verify pass** — `dotnet test server/AdaVoice.Server.Tests` → PASS.
- [ ] **Step 5: Commit** — `feat(server): add idempotent database seeder for plans and super_admin`

---

### Task 5: Raw-SQL / query-filter-bypass source guard (#16, #18)

**Files:**
- Test: `server/AdaVoice.Server.Tests/Architecture/RawSqlGuardTests.cs` (DB-less)

**Interfaces — Produces:** nothing consumed downstream; a standing guard.

- [ ] **Step 1: Write the failing test** — scan every `*.cs` under `server/` (exclude any `obj/` and the Tests project itself) for `FromSqlRaw(` and `.IgnoreQueryFilters(`. Fail listing any occurrence NOT immediately preceded by an allow-comment line containing `tenant-scan-ok:` (the deliberate-escape marker reserved for Phase 8 worker code per #17). Locate `server/` by walking up to `AdaVoice.slnx` (reuse the Phase-0 guard's root-finder pattern).

```csharp
[Fact]
public void No_unreviewed_FromSqlRaw_or_IgnoreQueryFilters_in_server_code()
{
    var offenders = ScanServerSources(new[] { "FromSqlRaw(", ".IgnoreQueryFilters(" });
    Assert.True(offenders.Count == 0,
        "Forbidden raw-SQL / filter-bypass without a `// tenant-scan-ok:` marker:\n" +
        string.Join("\n", offenders));
}
```

- [ ] **Step 2: Run to verify it fails** — temporarily add a `FromSqlRaw(` line to a server file → test FAILS; remove it → confirm the red-green.
- [ ] **Step 3: Implement the scanner** so the test passes at zero real occurrences.
- [ ] **Step 4: Run to verify it passes** — PASS.
- [ ] **Step 5: Commit** — `test(server): guard against unreviewed raw SQL and query-filter bypass`

---

### Task 6: CI — run server integration tests against PostgreSQL 16

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Split the windows job's test run** — change its Test step to `dotnet test AdaVoice.slnx --no-build --configuration Release --filter "Category!=Integration"` (existing app/core/audio tests are un-tagged and still run; server integration tests are skipped here — the windows runner has no Postgres).
- [ ] **Step 2: Add an `ubuntu-latest` job `server-tests`** with:
  - `services: postgres:` → `image: postgres:16`, env `POSTGRES_DB=adavoice POSTGRES_USER=adavoice POSTGRES_PASSWORD=adavoice_dev`, `ports: 5432:5432`, health `pg_isready` options.
  - Steps: checkout; `actions/setup-dotnet@v4` `10.0.x`; `dotnet test server/AdaVoice.Server.Tests/AdaVoice.Server.Tests.csproj --configuration Release` with env `ADAVOICE_DB_CONNECTION=Host=localhost;Port=5432;Database=adavoice;Username=adavoice;Password=adavoice_dev`. (This job builds only the server graph — all `net10.0`, no Windows deps — so it runs on Linux.)
- [ ] **Step 3: Verify locally (proxy for CI):**
  - Windows path: `dotnet test AdaVoice.slnx --filter "Category!=Integration"` → all green (existing suite unchanged).
  - Linux/server path (docker-compose up): `dotnet test server/AdaVoice.Server.Tests` → all green including `Category=Integration`.
- [ ] **Step 4: Commit** — `ci(server): run server integration tests against PostgreSQL 16 on ubuntu`

---

## Acceptance-criteria trace

| AC (roadmap Phase 1) | Verified by |
|---|---|
| `dotnet ef database update` creates all 13 tables with snake_case + timestamptz audit columns | Task 3 Step 5 command (documented) **and** `SchemaIntegrationTests` (Task 3) running in CI ubuntu job |
| A query without an explicit tenant filter returns only the current tenant's rows | `TenantIsolationTests` (Task 3, two-instance, real PG) |
| Running the seeder twice produces no duplicates | `SeederTests` idempotency (Task 4) |
| All Phase 1 tests green in CI | Task 6 ubuntu job (integration) + windows job (DB-less/guard) both green |

## Final verification (after Task 6)

1. `dotnet build AdaVoice.slnx -c Release` → 0 warnings / 0 errors.
2. `dotnet test AdaVoice.slnx --filter "Category!=Integration"` → existing suite + DB-less server tests green (proves desktop app unchanged).
3. docker-compose up; `dotnet test server/AdaVoice.Server.Tests` → all server tests green (integration included).
4. `dotnet ef database update ...` against docker-compose → 13 snake_case tables present; capture for handoff.
5. Confirm `AdaVoice.Core/Audio/Audio.Wasapi/Host/App` `.csproj` diffs are empty (no new refs).
6. Confirm `Directory.Packages.props` holds the 4 new versions; no `Version=` attributes in any `.csproj`.
7. superpowers:requesting-code-review (whole-branch) → then superpowers:verification-before-completion against the 4 ACs.
8. Update `handoff.md` (Phase 1 shipped, Phase 2 next); commit the plan into `docs/superpowers/plans/`.

## Deferred (explicitly NOT built in Phase 1)

Auth/login/JWT (Phase 2); subscription state machine (Phase 3); device-activation logic (Phase 4); license issuing (Phase 5); WPF client (Phase 6); billing (Phase 7); workers + the #17 worker-tenant test (Phase 8); admin panel (Phase 9); startup auto-migrate/seed wiring; table partitioning / soft-delete / `must_change_password` column.
