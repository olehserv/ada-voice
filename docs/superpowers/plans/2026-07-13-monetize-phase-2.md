# AdaVoice Monetization — Phase 2: Auth — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to
> implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. On execution, **first
> copy this plan into `docs/superpowers/plans/` and commit it** (repo convention +
> persist-plans-in-repo) — it is already there if you are reading it from that folder.

**Goal:** Users can log in and hold a session safely (ES256 JWT access token + rotating refresh
token with family revocation, lockout, rate limiting, RFC 7807 errors), and every auth event is
audited — built as the first real API surface on the Phase-1 database, with the desktop app and
its 459 tests untouched.

**Architecture:** The `AdaVoice.Server.Api` project stops being an inert scaffold and becomes a
real ASP.NET Core minimal-API host. Auth *orchestration + persistence* (user lookup, password
verify, lockout, refresh-token rotation, audit writing) live in `AdaVoice.Server.Infrastructure`
as services using EF Core + `PasswordHasher` + `System.Security.Cryptography` (no ASP.NET / no
JOSE dependency). JWT *issuance and validation* live in `Api` (it has `JwtBearer`, hence the
IdentityModel handler transitively). A request-scoped `ITenantProvider` reads the JWT `tenant_id`
claim so authenticated queries flow through the Phase-1 global filters; anonymous endpoints
(login/refresh) look the user up with a deliberate, marked `IgnoreQueryFilters` bypass.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, `Microsoft.AspNetCore.Authentication.JwtBearer`
(token validation + transitive `Microsoft.IdentityModel.JsonWebTokens` for issuance), EF Core 10 /
Npgsql, `Microsoft.Extensions.Identity.Core` `PasswordHasher` (Phase 1), built-in
`RateLimiter` + `ProblemDetails` + `IExceptionHandler`. Tests: xUnit + the Phase-1 `PostgresFixture`
+ `Microsoft.AspNetCore.Mvc.Testing` `WebApplicationFactory`. Central package management,
`TreatWarningsAsErrors=true`.

## Global Constraints

Every task's requirements implicitly include this section. Copy the binding items into each
reviewer prompt.

- **Existing tests stay green at every commit.** The desktop app's behavior must NOT change. No new
  package/project reference on `AdaVoice.Core`, `AdaVoice.Audio`, `AdaVoice.Audio.Wasapi`,
  `AdaVoice.Host`, or `AdaVoice.App`. `AdaVoice.Licensing` is Phase 6 — do not create it here.
- **Locked dependency direction:** `Api → Infrastructure → Domain`; `Workers → Infrastructure`.
  Domain keeps ZERO project references. The Phase-0 `DependencyDirectionTests` guard stays green.
  The `Tests` project may reference `Api` (allowed; the guard does not cover the test project).
- **Package versions go in `Directory.Packages.props` only**, never in `.csproj`. Approved Phase-2
  additions (exact versions, owner-approved 2026-07-13):
  - `Microsoft.AspNetCore.Authentication.JwtBearer` **10.0.9** → `Api`
  - `Microsoft.AspNetCore.Mvc.Testing` **10.0.9** → `Tests`
  - No third package. Token *issuance* uses `Microsoft.IdentityModel.JsonWebTokens`, which arrives
    transitively via `JwtBearer` in the `Api` project — do not add it separately, and keep all code
    that touches IdentityModel types inside `Api`.
- **No schema change, no migration.** The Phase-1 schema already has every column this phase needs
  (`users.failed_login_count`, `users.locked_until`, `users.last_login_at`; the full
  `refresh_tokens` table; `audit_logs`). If any task appears to need a new column, STOP and ask —
  it means a spec was misread.
- **Secrets from env vars only.** The ES256 access-token signing key (PEM) and its `kid` come from
  env vars (`ADAVOICE_JWT_SIGNING_KEY`, `ADAVOICE_JWT_KID`) with **no default private key baked in**.
  Never log passwords, refresh tokens, access tokens, or the signing key. Non-secret JWT config
  (issuer, audience, lifetimes, lockout threshold, rate-limit permits) may live in `appsettings.json`.
- **Canonical is `docs/monetize/README.md`**; on conflict the brief wins. Auth specifics:
  `api-design.md` (endpoints, error `code`s, login note), `security-design.md` §3/§8/§14.
  Deviating from a canonical decision needs a new ADR + owner approval.
- **Build only what Phase 2 lists.** No `signing_keys` table use, no JWKS endpoint, no license
  tickets, no device binding, no admin CRUD, no Razor pages, no subscription logic, no workers, no
  `/healthz` (all later phases). `deviceId` is accepted in the login DTO for forward-compat but the
  refresh token is **not** device-bound this phase (no `device_activations` rows exist until Phase 4;
  leave `RefreshToken.DeviceActivationId` null). "Force password change on first login" is a Phase-1
  decision note, not a Phase-2 criterion — set `last_login_at` on success but build no enforcement.

## Phase-2 decisions (record here; owner-approved 2026-07-13)

- **Access-token signing key from env var (not the Phase-5 `signing_keys` table).** Canon fixes
  ES256 + `kid` header but is silent on the key source. The server both signs and validates access
  tokens in-process, so no JWKS/pinning is needed yet. Reversible: Phase 5 may unify JWT signing
  with `signing_keys` if desired. Not a canonical deviation.
- **Login resolves the user by a global email lookup.** The login DTO carries no tenant and email is
  unique only *per tenant*. Look the user up by email across all tenants with `IgnoreQueryFilters`
  (marked `tenant-scan-ok:`); require exactly one **active** match; on zero OR multiple matches return
  the same generic auth-failure (no enumeration). No schema change. Known MVP limit: two tenants
  sharing an email make that login ambiguous and fail generically. Acceptable for the pilot.
- **Logging via `Microsoft.Extensions.Logging` (`ILogger<T>`), Serilog wiring deferred.** The §14 #6
  and #20 pitfalls are behavioral (never leak secrets/exceptions to the client; full exception to the
  log only) and are satisfied with the built-in logging abstraction. The concrete Serilog server sink
  is operational wiring deferred to Phase 10/11. The durable record for this phase is `audit_logs`.
- **Rate-limit client IP = connection remote IP for now.** `ForwardedHeaders`
  (`KnownProxies`/`KnownNetworks`) is a deployment-topology concern (§14 #22, tagged Phase 2/10);
  configured in Phase 10 against the real topology. A code comment marks the seam; a test proves the
  429 path fires.
- **JWT identifiers:** `iss = adavoice-auth`, `aud = adavoice-api` (distinct from the license
  ticket's `adavoice-license`/`adavoice-desktop`). Claims: `sub` = userId, `tenant_id`, `role`
  (text), `jti`. `RoleClaimType = "role"`, `NameClaimType = "sub"`.
- **Error `code`s:** reuse the canonical ones where they exist (`unauthorized`,
  `invalid_refresh_token`, `forbidden`, `rate_limited`). Login failure (wrong password / unknown
  email / locked) returns the single generic code **`invalid_credentials`** (401). A body-mismatch
  on idempotency etc. is out of scope this phase.

## §14 Security-pitfall coverage (each becomes a test or an explicit code-review point)

| Pitfall | Tag | How Phase 2 covers it |
|---|---|---|
| **#1** Default clock skew extends token lifetime | Phase 2 | `TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(1)` set explicitly. Tests assert the configured skew and that access-token `exp = iat + 15min`; an already-expired token is rejected (Task 2 + Task 5). |
| **#2** Accepting whatever `alg` the header claims | Phase 2/6 | `ValidAlgorithms = ["ES256"]` on validation. Test: a token whose header alg is swapped (`none` / `HS256`) fails validation before the payload is trusted (Task 2). |
| **#3** Refresh rotation race logs out honest users | Phase 2/6 | Rotation runs in one transaction with `SELECT … FOR UPDATE` (via `FromSql` interpolated, allowed by the guard) on the token row, so two concurrent uses cannot both succeed. Tests: rotation happy path, replay → family revoked, and a concurrency test (two parallel refreshes → exactly one 200) (Task 4). |
| **#4** User enumeration through different answers | Phase 2 | One generic `invalid_credentials` response for wrong password, unknown email, and locked account (SEC-03). Unknown email still runs a dummy `PasswordHasher.VerifyHashedPassword` so timing matches. Test: unknown-email and wrong-password responses are byte-identical (status, code, body) (Task 3). |
| **#5** Lockout counter updated read-modify-write | Phase 2 | Failure increments via one atomic `ExecuteUpdateAsync` (`FailedLoginCount + 1`); the lock is set with a conditional atomic `ExecuteUpdateAsync`. Tests: N parallel wrong passwords produce count == N; the 10th failure locks; 11th and a correct password during the window both fail generically (Task 5). |
| **#6** Tokens and passwords leak into logs | Phase 2/6 | No request-body logging on `/api/auth/*`; auth DTOs never destructured; `Authorization` redacted if any HTTP logging is added. Test: a capturing logger sees no password / refresh-token / access-token substring after login + refresh (Task 6). |
| **#20** Error responses leak internals | Phase 2 | One `IExceptionHandler`: generic `detail` + `correlationId` to the caller, full exception to `ILogger` only. Test: forced exception → ProblemDetails contains no exception/stack text and carries a `correlationId` (Task 1). |
| **#22** IP rate limiting trusts forwarded headers | Phase 2/10 | Partition by `HttpContext.Connection.RemoteIpAddress` now; a comment marks that `ForwardedHeaders` (`KnownProxies`/`KnownNetworks`) is Phase-10 deployment config. Test: >limit requests/min → 429 `rate_limited` with `Retry-After` (Task 6). Code-review point: no forwarded-header trust added this phase. |

## File Structure

```text
server/AdaVoice.Server.Infrastructure/
  Auth/
    IAuditWriter.cs / AuditWriter.cs            # append audit_logs, explicit tenant_id
    IUserAuthenticationService.cs / UserAuthenticationService.cs  # global email lookup (bypass), lockout, counters
    IRefreshTokenService.cs / RefreshTokenService.cs             # opaque token, SHA-256, rotate (FOR UPDATE), family revoke
    AuthResults.cs                              # result enums/records shared with Api (no ASP.NET types)
    ICorrelationContext.cs                      # scoped correlation id, set by Api middleware
server/AdaVoice.Server.Api/
  Program.cs                                    # real host: DI, middleware, endpoints
  Auth/
    JwtOptions.cs                               # bound from config (issuer/audience/kid/lifetime)
    JwtKeyProvider.cs                           # env-var PEM -> ECDsaSecurityKey (+ kid)
    IAccessTokenIssuer.cs / AccessTokenIssuer.cs # 15-min ES256 JWT, claims, kid header
    AuthEndpoints.cs                            # MapAuthEndpoints: login/refresh/logout/change-password/me
    AuthDtos.cs                                 # request/response records
    AuthProblems.cs                             # RFC 7807 helpers (type/title/status/code/correlationId)
    HttpContextTenantProvider.cs                # ITenantProvider from JWT tenant_id claim
  Infrastructure/
    CorrelationIdMiddleware.cs + CorrelationContext.cs  # X-Correlation-Id in/out, scoped accessor
    GlobalExceptionHandler.cs                   # #20
    AuthRateLimit.cs                            # fixed-window per-IP policy for /api/auth/*
  appsettings.json                              # non-secret JWT + lockout + rate-limit config
server/AdaVoice.Server.Tests/
  Auth/ExceptionHandlingTests.cs                # DB-less (#20, correlation id)
  Auth/AccessTokenTests.cs                      # DB-less (#1, #2, token shape)
  Auth/AuthApiFactory.cs                        # WebApplicationFactory<Program>: per-test DB + ephemeral ES256 key + config overrides
  Auth/LoginTests.cs                            # [Integration] happy path, wrong pw, unknown email (#4), audit
  Auth/RefreshTokenTests.cs                     # [Integration] rotation, reuse->family revoke (AC1), concurrency (#3), logout
  Auth/LockoutTests.cs                          # [Integration] AC2, atomic counter (#5)
  Auth/AccountEndpointsTests.cs                 # [Integration] change-password (revokes families), /me, expired token (AC3)
  Auth/RateLimitAndLoggingTests.cs             # [Integration] 429 (#22), no-secret-in-log (#6), audit completeness (AC4)
```

Integration tests carry `[Trait("Category","Integration")]` and run in the existing CI ubuntu
`server-tests` job (which runs the whole server test project, unfiltered). DB-less tests do not
carry the trait and also run in the Windows job. **No `ci.yml` change is needed** — do not touch it.

---

### Task 1: Api host skeleton — config, correlation id, ProblemDetails, global exception handler

Turns the inert `Program.cs` into a real host with the cross-cutting plumbing every endpoint needs.
No auth logic yet.

**Files:**
- Modify: `Directory.Packages.props` (add the two approved `PackageVersion` entries);
  `server/AdaVoice.Server.Api/AdaVoice.Server.Api.csproj` (add `JwtBearer` `PackageReference`, no
  version); `server/AdaVoice.Server.Tests/AdaVoice.Server.Tests.csproj` (add `Mvc.Testing`
  `PackageReference` + a `<ProjectReference>` to `Api`)
- Modify: `server/AdaVoice.Server.Api/Program.cs`
- Create: `server/AdaVoice.Server.Api/Infrastructure/CorrelationContext.cs`,
  `.../CorrelationIdMiddleware.cs`, `.../GlobalExceptionHandler.cs`;
  `server/AdaVoice.Server.Infrastructure/Auth/ICorrelationContext.cs`;
  `server/AdaVoice.Server.Api/appsettings.json`
- Test: `server/AdaVoice.Server.Tests/Auth/ExceptionHandlingTests.cs` (DB-less)

**Interfaces — Produces:**
- `ICorrelationContext { string CorrelationId { get; } }` (in Infrastructure/Auth) with an Api
  `CorrelationContext` scoped implementation whose value the middleware sets. Consumed by
  `AuditWriter` (Task 3) and `GlobalExceptionHandler`.
- `CorrelationIdMiddleware`: reads request header `X-Correlation-Id` (or generates a GUID string),
  stores it on the scoped `CorrelationContext`, and echoes it on the response header.
- `GlobalExceptionHandler : IExceptionHandler`: logs the exception via `ILogger`, then writes a
  generic `application/problem+json` (status 500, `title = "An unexpected error occurred."`, a
  generic `detail`, `code = "internal_error"`, and the `correlationId` extension). It writes **no**
  exception message, type, or stack trace into the response.
- `Program.cs` registers: `AddProblemDetails()`, `AddHttpContextAccessor()`, the DbContext
  (`AddDbContext<AdaVoiceDbContext>(UseNpgsql(conn).UseSnakeCaseNamingConvention())` reading
  `ADAVOICE_DB_CONNECTION`), `AddScoped<CorrelationContext>` bound to both `ICorrelationContext` and
  itself, `AddExceptionHandler<GlobalExceptionHandler>()`. Pipeline: `UseExceptionHandler()` →
  `CorrelationIdMiddleware` → (auth/ratelimit added in later tasks). Add `public partial class
  Program { }` at the end so `WebApplicationFactory<Program>` can bind.
- `appsettings.json`: a `Jwt` section (`Issuer`, `Audience`, `Kid`, `AccessTokenMinutes: 15`), an
  `Auth` section (`MaxFailedLogins: 10`, `LockoutMinutes: 15`, `RefreshSlidingDays: 30`,
  `RefreshAbsoluteDays: 90`), a `RateLimit` section (`AuthPermitPerMinute: 10`). No secrets here.

- [ ] **Step 1: Write the failing test** — `ExceptionHandlingTests.cs` (DB-less; drive the handler
  and middleware directly with a `DefaultHttpContext`):

```csharp
using System.Text.Json;
using AdaVoice.Server.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

public class ExceptionHandlingTests
{
    [Fact]
    public async Task Handler_writes_generic_problem_without_exception_text()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        var correlation = new CorrelationContext { CorrelationId = "corr-123" };
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, correlation);

        var secret = "SUPER-SECRET-STACK-marker";
        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException(secret), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(500, ctx.Response.StatusCode);
        Assert.StartsWith("application/problem+json", ctx.Response.ContentType);
        body.Position = 0;
        var json = await new StreamReader(body).ReadToEndAsync();
        Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain("InvalidOperationException", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("corr-123", doc.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("internal_error", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Middleware_generates_and_echoes_correlation_id()
    {
        var ctx = new DefaultHttpContext();
        var correlation = new CorrelationContext();
        var mw = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await mw.InvokeAsync(ctx, correlation);

        Assert.False(string.IsNullOrWhiteSpace(correlation.CorrelationId));
        Assert.Equal(correlation.CorrelationId, ctx.Response.Headers["X-Correlation-Id"].ToString());
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test server/AdaVoice.Server.Tests --filter Category!=Integration` → FAIL (types don't exist / won't compile).
- [ ] **Step 3: Implement** — add the packages centrally + the two `PackageReference`s + the
  `Tests → Api` project reference; write `CorrelationContext` (mutable `CorrelationId`, default via
  `Guid.NewGuid().ToString()` on first read is fine but the middleware sets it explicitly),
  `CorrelationIdMiddleware` (constructor `RequestDelegate next`; `InvokeAsync(HttpContext,
  CorrelationContext)` sets id from the request header or a new GUID, writes the response header via
  `ctx.Response.OnStarting`, calls `next`), `GlobalExceptionHandler` (writes the generic problem
  JSON, logs `ex` at Error), the `Program.cs` host wiring, and `appsettings.json`. `ICorrelationContext`
  goes in Infrastructure so services can depend on it without referencing Api.
- [ ] **Step 4: Run to verify it passes** — targeted test PASS; `dotnet build AdaVoice.slnx -c Release`
  → 0 warnings / 0 errors; Phase-0 `DependencyDirectionTests` still green.
- [ ] **Step 5: Commit** — `feat(server): add Api host skeleton with correlation id and safe error handling`

---

### Task 2: ES256 access-token issuance and JWT validation config (#1, #2)

**Files:**
- Create: `server/AdaVoice.Server.Api/Auth/JwtOptions.cs`, `.../JwtKeyProvider.cs`,
  `.../IAccessTokenIssuer.cs`, `.../AccessTokenIssuer.cs`
- Modify: `server/AdaVoice.Server.Api/Program.cs` (bind `JwtOptions`, register `JwtKeyProvider` +
  `IAccessTokenIssuer`, add `AddAuthentication().AddJwtBearer(...)` with the strict validation
  parameters, add `UseAuthentication()`/`UseAuthorization()` to the pipeline, `AddAuthorization()`)
- Test: `server/AdaVoice.Server.Tests/Auth/AccessTokenTests.cs` (DB-less)

**Interfaces:**
- Consumes: `JwtOptions` (from config), the env var `ADAVOICE_JWT_SIGNING_KEY` (EC private key PEM,
  PKCS#8 or SEC1) and `ADAVOICE_JWT_KID`.
- Produces: `JwtKeyProvider` exposing `ECDsaSecurityKey SigningKey { get; }` (private, `KeyId = kid`)
  and `ECDsaSecurityKey PublicKey { get; }` for validation; `IAccessTokenIssuer.Issue(Guid userId,
  Guid tenantId, string roleText) → (string Token, DateTimeOffset ExpiresAt)`. Consumed by the login
  and refresh endpoints (Tasks 3–4) and the JwtBearer validation config.

Key rules:
- `JwtKeyProvider` reads the PEM from the env var; `var ec = ECDsa.Create(); ec.ImportFromPem(pem);`
  wrap as `new ECDsaSecurityKey(ec) { KeyId = kid }`. Throw a clear startup exception if the env var
  is missing (fail fast; never fall back to a baked-in key). The public key is the same
  `ECDsaSecurityKey` (ECDsa holds both halves) — reuse it for validation.
- `AccessTokenIssuer` uses `JsonWebTokenHandler` (from `Microsoft.IdentityModel.JsonWebTokens`,
  transitive via JwtBearer). `SecurityTokenDescriptor` with `Issuer`, `Audience`,
  `Expires = UtcNow + AccessTokenMinutes`, `SigningCredentials = new(key, SecurityAlgorithms.EcdsaSha256)`,
  and claims `sub`, `tenant_id`, `role`, `jti` (new GUID). `ExpiresAt` returned is the same instant
  used for `Expires`.
- JwtBearer `TokenValidationParameters`: `ValidIssuer`, `ValidAudience`, `IssuerSigningKey =
  provider.PublicKey`, `ValidAlgorithms = ["ES256"]`, `ValidateIssuerSigningKey = true`,
  `ValidateLifetime = true`, `ClockSkew = TimeSpan.FromMinutes(1)`, `RoleClaimType = "role"`,
  `NameClaimType = "sub"`.

- [ ] **Step 1: Write the failing test** — `AccessTokenTests.cs` (generate an ephemeral P-256 key in
  the test; build the issuer directly; validate with `JsonWebTokenHandler`):

```csharp
using System.Security.Cryptography;
using AdaVoice.Server.Api.Auth;
using AdaVoice.Server.Domain.Enums;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

public class AccessTokenTests
{
    private static (IAccessTokenIssuer issuer, ECDsaSecurityKey key) NewIssuer()
    {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new ECDsaSecurityKey(ec) { KeyId = "test-kid" };
        var opts = new JwtOptions { Issuer = "adavoice-auth", Audience = "adavoice-api", Kid = "test-kid", AccessTokenMinutes = 15 };
        return (new AccessTokenIssuer(new StubKeyProvider(key), opts), key);
    }

    [Fact]
    public async Task Issued_token_is_es256_with_kid_and_expected_claims_and_15min_lifetime()
    {
        var (issuer, key) = NewIssuer();
        var userId = Guid.NewGuid(); var tenantId = Guid.NewGuid();

        var (token, expiresAt) = issuer.Issue(userId, tenantId, "super_admin");

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "adavoice-auth", ValidAudience = "adavoice-api",
            IssuerSigningKey = key, ValidAlgorithms = ["ES256"],
            ClockSkew = TimeSpan.FromMinutes(1),
        });
        Assert.True(result.IsValid);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal("ES256", jwt.Alg);
        Assert.Equal("test-kid", jwt.Kid);
        Assert.Equal(userId.ToString(), jwt.GetClaim("sub").Value);
        Assert.Equal(tenantId.ToString(), jwt.GetClaim("tenant_id").Value);
        Assert.Equal("super_admin", jwt.GetClaim("role").Value);
        Assert.False(string.IsNullOrEmpty(jwt.GetClaim("jti").Value));
        // 15-minute lifetime (allow a few seconds of issue-time slack).
        Assert.InRange((expiresAt - jwt.ValidFrom).TotalMinutes, 14.5, 15.5);
    }

    [Fact]
    public async Task Validation_rejects_a_non_es256_algorithm()
    {
        var (issuer, key) = NewIssuer();
        var (token, _) = issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), "operator");
        // Forge a header that claims alg=none by swapping the first segment.
        var parts = token.Split('.');
        var noneHeader = Base64UrlEncoder.Encode("""{"alg":"none","kid":"test-kid","typ":"JWT"}""");
        var forged = $"{noneHeader}.{parts[1]}.";
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(forged, new TokenValidationParameters
        {
            ValidIssuer = "adavoice-auth", ValidAudience = "adavoice-api",
            IssuerSigningKey = key, ValidAlgorithms = ["ES256"], ClockSkew = TimeSpan.FromMinutes(1),
        });
        Assert.False(result.IsValid);
    }
}
```

> The test needs a tiny `StubKeyProvider : IJwtKeyProvider` returning the ephemeral key. Define
> `IJwtKeyProvider { ECDsaSecurityKey SigningKey; ECDsaSecurityKey PublicKey; }` in Api so both the
> real `JwtKeyProvider` and the test stub implement it, and `AccessTokenIssuer` depends on the
> interface. Put `StubKeyProvider` in the test file.

- [ ] **Step 2: Run to verify it fails** — FAIL (types missing).
- [ ] **Step 3: Implement** — `JwtOptions`, `IJwtKeyProvider` + `JwtKeyProvider`, `IAccessTokenIssuer`
  + `AccessTokenIssuer`; wire `AddJwtBearer` + `AddAuthorization` + `UseAuthentication/UseAuthorization`
  in `Program.cs`.
- [ ] **Step 4: Run to verify it passes** — targeted DB-less tests PASS; `dotnet build AdaVoice.slnx -c Release` → 0/0.
- [ ] **Step 5: Commit** — `feat(server): add ES256 access-token issuance and strict JWT validation`

---

### Task 3: Login endpoint — user lookup, password verify, generic failure (#4), audit, first refresh token

**Files:**
- Create: `server/AdaVoice.Server.Infrastructure/Auth/AuthResults.cs`,
  `IAuditWriter.cs`/`AuditWriter.cs`, `IUserAuthenticationService.cs`/`UserAuthenticationService.cs`,
  `IRefreshTokenService.cs`/`RefreshTokenService.cs`
- Create: `server/AdaVoice.Server.Api/Auth/AuthDtos.cs`, `AuthProblems.cs`, `AuthEndpoints.cs`,
  `HttpContextTenantProvider.cs`
- Modify: `Program.cs` (register the Infrastructure auth services + `HttpContextTenantProvider` as the
  scoped `ITenantProvider`; `MapAuthEndpoints()`)
- Test: `server/AdaVoice.Server.Tests/Auth/AuthApiFactory.cs`,
  `server/AdaVoice.Server.Tests/Auth/LoginTests.cs` `[Integration]`

**Interfaces — Produces:**
- `IUserAuthenticationService`:
  - `Task<User?> FindActiveUserByEmailAsync(string email, CancellationToken)` — **filter-bypassed**
    global lookup: `_db.Users.IgnoreQueryFilters()` (with a `// tenant-scan-ok:` marker directly
    above) `.Where(u => u.Email == email && u.Status == UserStatus.Active)`; return the single match
    or null (0 or >1 → null). Email compares case-insensitively (the column is `citext`).
  - `Task RegisterFailedAttemptAsync(Guid userId, CancellationToken)` — atomic increment
    (`ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1))`),
    then a conditional atomic lock:
    `_db.Users.Where(u => u.Id == userId && u.FailedLoginCount >= max && u.LockedUntil == null)
    .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockedUntil, now + lockout))` — returns whether the
    account was just locked (rows-affected > 0) so the endpoint can audit `auth.account_locked`.
  - `Task RegisterSuccessAsync(Guid userId, CancellationToken)` — one `ExecuteUpdateAsync` resetting
    `FailedLoginCount = 0`, `LockedUntil = null`, `LastLoginAt = now`.
  - A helper to check "is currently locked": `bool IsLocked(User u, DateTimeOffset now) => u.LockedUntil is { } t && t > now;`
- `IRefreshTokenService`:
  - `Task<IssuedRefreshToken> IssueNewFamilyAsync(Guid userId, CancellationToken)` — returns
    `IssuedRefreshToken(string RawToken, DateTimeOffset ExpiresAt)`; creates a `RefreshToken` with a
    new `FamilyId`, `TokenHash = Sha256Base64Url(raw)`, `IssuedAt = now`,
    `ExpiresAt = now + slidingDays`, `DeviceActivationId = null`. `RawToken` = 32 random bytes
    (`RandomNumberGenerator.GetBytes(32)`) base64url. **Only the hash is stored.**
  - (rotation/reuse APIs land in Task 4.)
- `IAuditWriter.WriteAsync(string action, string entityType, Guid? entityId, Guid? tenantId,
  Guid? actorUserId, ActorType actorType, string? dataJson = null, CancellationToken ct = default)` —
  adds an `AuditLog` row with `Ip` and `CorrelationId` pulled from injected `IHttpContextAccessor` +
  `ICorrelationContext`; sets `TenantId` **explicitly** (AuditLog is not `IHasTenant`, so the
  interceptor won't stamp it). `CreatedAt` is stamped by the Phase-1 interceptor (`IHasCreatedAt`).
- `HttpContextTenantProvider : ITenantProvider` — `CurrentTenantId` parses the `tenant_id` claim from
  `IHttpContextAccessor.HttpContext?.User`; returns null when unauthenticated (login/refresh path),
  which is exactly what makes the filtered `users` query return nothing, so those endpoints use the
  bypass lookup above.
- `AuthDtos`: `record LoginRequest(string Email, string Password, Guid? DeviceId);`
  `record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken,
  DateTimeOffset RefreshTokenExpiresAt);`
- `AuthProblems`: static helpers returning `IResult` via `Results.Problem(...)`, each with `type`,
  `title`, `status`, and extensions `{ code, correlationId }`. `InvalidCredentials()` → 401,
  code `invalid_credentials`. `Unauthorized()` → 401 code `unauthorized`.
  `InvalidRefreshToken()` → 401 code `invalid_refresh_token`. `RateLimited(retryAfter)` → 429
  code `rate_limited`. (`correlationId` read from `ICorrelationContext`.)

**Login flow (the endpoint):**
1. Look up the active user by email.
2. Always run `PasswordHasher.VerifyHashedPassword` — against the found user's hash, or against a
   fixed dummy hash constant when no user (constant-time defense, #4). A private static readonly
   dummy hash string is computed once at startup (`hasher.HashPassword(dummyUser, "dummy")`).
3. Compute `bool ok = user is not null && verify == Success && !IsLocked(user, now)`.
   - If `user is null` OR password mismatch → if `user` exists, `RegisterFailedAttemptAsync` (audit
     `auth.login_failed`, and `auth.account_locked` if it just locked); audit `auth.login_failed`
     with `TenantId = user?.TenantId` (null for unknown email). Return `AuthProblems.InvalidCredentials()`.
   - If password matches but `IsLocked` → audit `auth.login_failed` (do NOT reset counters); return
     the identical `InvalidCredentials()` (SEC-03: locked looks like wrong password).
   - Else (ok) → `RegisterSuccessAsync`; issue access token (`IAccessTokenIssuer`); issue a new
     refresh-token family; audit `auth.login_succeeded`; return `200 TokenResponse`.
4. `deviceId` is read from the DTO but not used to bind the token this phase (documented).

- [ ] **Step 1: Write `AuthApiFactory` + failing `LoginTests`** — the factory is the reusable test
  harness for all integration tasks:

```csharp
// AuthApiFactory.cs
using System.Security.Cryptography;
using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    public int AuthPermitPerMinute { get; init; } = 1000; // high by default so functional tests don't hit 429
    public ECDsa Signing { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public AuthApiFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var pem = Signing.ExportPkcs8PrivateKeyPem();
        Environment.SetEnvironmentVariable("ADAVOICE_JWT_SIGNING_KEY", pem);
        Environment.SetEnvironmentVariable("ADAVOICE_JWT_KID", "test-kid");
        builder.UseSetting("ADAVOICE_DB_CONNECTION", _connectionString);
        builder.UseSetting("RateLimit:AuthPermitPerMinute", AuthPermitPerMinute.ToString());
        builder.ConfigureServices(services =>
        {
            // Point the DbContext at the per-test fixture database.
            services.RemoveAll<DbContextOptions<AdaVoiceDbContext>>();
            services.AddDbContext<AdaVoiceDbContext>(o =>
                o.UseNpgsql(_connectionString).UseSnakeCaseNamingConvention());
        });
    }
}
```

> `Program.cs` must read the connection string via `builder.Configuration["ADAVOICE_DB_CONNECTION"]`
> (falling back to the env var) so `UseSetting` overrides it, and read the signing PEM from the env
> var. Confirm both during implementation.

```csharp
// LoginTests.cs  (uses the Phase-1 PostgresFixture pattern; seed a user directly via a context)
[Trait("Category", "Integration")]
public class LoginTests : IClassFixture<PostgresFixture>
{
    // Arrange helper: seed a tenant + active user with a known password via a DbContext built on
    // the fixture connection and the real PasswordHasher; then create AuthApiFactory(fixture.ConnectionString).

    [Fact] public async Task Login_with_correct_password_returns_tokens_and_audits_success() { /* 200; body has all 4 fields; access token validates against factory.Signing public key; refresh token row exists (hash != raw); last_login_at set; audit_logs has auth.login_succeeded */ }

    [Fact] public async Task Wrong_password_returns_generic_401_and_increments_counter() { /* 401 code invalid_credentials; failed_login_count == 1; audit auth.login_failed */ }

    [Fact] public async Task Unknown_email_returns_the_same_response_as_wrong_password() {
        // Assert status, code, and response body are byte-identical between an unknown-email attempt
        // and a wrong-password attempt (ignoring correlationId, which differs per request). (#4)
    }
}
```

- [ ] **Step 2: Run to verify they fail** — endpoints/services don't exist → FAIL.
- [ ] **Step 3: Implement** — the four Infrastructure services + Api DTOs/problems/endpoints + the
  `HttpContextTenantProvider` registration + `MapAuthEndpoints`. Register the auth endpoint group but
  do not attach rate limiting yet (Task 6).
- [ ] **Step 4: Run to verify pass** — `dotnet test server/AdaVoice.Server.Tests` (docker-compose PG16
  up) → PASS incl. `Category=Integration`; `dotnet build AdaVoice.slnx -c Release` → 0/0;
  `RawSqlGuardTests` green (the bypass carries the `tenant-scan-ok:` marker);
  `DependencyDirectionTests` green.
- [ ] **Step 5: Commit** — `feat(server): add login endpoint with generic auth failure and audit`

---

### Task 4: Refresh rotation + reuse detection (#3, AC1) and logout

**Files:**
- Modify: `server/AdaVoice.Server.Infrastructure/Auth/RefreshTokenService.cs` (add rotate/reuse/revoke)
- Modify: `server/AdaVoice.Server.Api/Auth/AuthEndpoints.cs` (add `refresh`, `logout`)
- Test: `server/AdaVoice.Server.Tests/Auth/RefreshTokenTests.cs` `[Integration]`

**Interfaces — Produces (on `IRefreshTokenService`):**
- `Task<RotationOutcome> RotateAsync(string rawToken, CancellationToken)`. Inside one transaction
  (`await using var tx = await _db.Database.BeginTransactionAsync(ct)`):
  1. `hash = Sha256Base64Url(rawToken)`.
  2. Lock the row:
     `var row = await _db.RefreshTokens.FromSql($"SELECT * FROM refresh_tokens WHERE token_hash = {hash} FOR UPDATE").FirstOrDefaultAsync(ct);`
     (`FromSql` interpolated is parameterized and allowed by `RawSqlGuardTests`; do NOT use `FromSqlRaw`.)
  3. If `row is null` → return `RotationOutcome.NotFound`.
  4. If `row.RevokedAt is not null` OR `row.ReplacedById is not null` → **reuse**: revoke the whole
     family (`_db.RefreshTokens.Where(r => r.FamilyId == row.FamilyId && r.RevokedAt == null)
     .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, now))`), commit, return
     `RotationOutcome.Reuse(row.UserId, row.FamilyId)`.
  5. If `row.ExpiresAt <= now` OR `now > familyFirstIssuedAt + absoluteDays` → return
     `RotationOutcome.Expired` (revoke the row). `familyFirstIssuedAt` = min `IssuedAt` in the family.
  6. Else rotate: create the replacement `RefreshToken` (same `FamilyId`, new hash/raw,
     `ExpiresAt = min(now + slidingDays, familyFirstIssuedAt + absoluteDays)`); set
     `row.RevokedAt = now`, `row.ReplacedById = new.Id`. Save, commit. Return
     `RotationOutcome.Rotated(userId, new RawToken, new ExpiresAt)`.
- `Task RevokeFamilyByRawAsync(string rawToken, CancellationToken)` for logout — hash, find the row
  (no lock needed), revoke its whole family; no-op if unknown.

**Endpoints:**
- `POST /api/auth/refresh` (anonymous, body `{ refreshToken }`): call `RotateAsync`. `Rotated` →
  issue a fresh access token for `userId` (load the user via the bypass lookup by id, or carry the
  role — simplest: `RotateAsync` returns the `userId`, then load `User` with `IgnoreQueryFilters` by
  id, marked `tenant-scan-ok:`, to read tenant/role for the access token), audit
  `auth.token_refreshed`, return `200 TokenResponse`. `Reuse` → audit `auth.refresh_reuse_detected`,
  return `401 invalid_refresh_token`. `NotFound`/`Expired` → `401 invalid_refresh_token` (no audit
  noise beyond a debug log).
- `POST /api/auth/logout` (authenticated, body `{ refreshToken }`): `RevokeFamilyByRawAsync`; audit
  `auth.logout` with the caller's `sub`/`tenant_id`; return `204`.

- [ ] **Step 1: Write failing `RefreshTokenTests`:**

```csharp
[Trait("Category", "Integration")]
public class RefreshTokenTests : IClassFixture<PostgresFixture>
{
    [Fact] public async Task Refresh_rotates_and_old_token_replay_revokes_the_family() {
        // login -> refresh (200, new tokens) -> replay the ORIGINAL refresh token
        //   -> 401 invalid_refresh_token AND the just-issued new token now also fails (family revoked). (AC1)
    }

    [Fact] public async Task Two_concurrent_refreshes_of_the_same_token_yield_exactly_one_success() {
        // Fire two POST /refresh with the same token via Task.WhenAll; assert exactly one 200 and one 401,
        // and that the family is revoked afterwards. (#3 — FOR UPDATE serialization)
    }

    [Fact] public async Task Logout_revokes_the_family() {
        // login -> logout(refreshToken) -> refresh(refreshToken) -> 401.
    }

    [Fact] public async Task Reuse_detection_writes_an_audit_row() {
        // after the reuse case, audit_logs contains auth.refresh_reuse_detected.
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** the rotation service + endpoints.
- [ ] **Step 4: Run to verify pass** — targeted integration tests PASS; guard + build green.
- [ ] **Step 5: Commit** — `feat(server): add refresh-token rotation, reuse detection, and logout`

---

### Task 5: Lockout (#5, AC2), change-password, /me, expired-token rejection (AC3)

**Files:**
- Modify: `server/AdaVoice.Server.Api/Auth/AuthEndpoints.cs` (add `change-password`, `me`)
- Modify: `server/AdaVoice.Server.Infrastructure/Auth/UserAuthenticationService.cs` /
  `RefreshTokenService.cs` (change-password support: verify old, set new hash, revoke all families)
- Test: `server/AdaVoice.Server.Tests/Auth/LockoutTests.cs`,
  `server/AdaVoice.Server.Tests/Auth/AccountEndpointsTests.cs` (both `[Integration]`)

**Interfaces — Produces:**
- `change-password` (authenticated, body `{ currentPassword, newPassword }`): load the current user
  by `sub` through the **filtered** context (the JWT tenant claim is set, so no bypass needed); verify
  `currentPassword`; on success set `PasswordHash = hasher.HashPassword(user, newPassword)`, save, and
  revoke ALL of the user's refresh families
  (`_db.RefreshTokens.Where(r => r.UserId == id && r.RevokedAt == null).ExecuteUpdateAsync(... RevokedAt = now)`);
  audit `auth.password_changed`; return `204`. Wrong current password → `401 invalid_credentials`.
- `GET /api/auth/me` (authenticated): return `{ userId, email, role, tenantId, displayName }` for the
  current user loaded through the filtered context. The `HttpContextTenantProvider` supplies the
  tenant, so the global filter matches the caller's own row.
- The lockout behavior itself was implemented in Task 3 (`RegisterFailedAttemptAsync`); this task
  proves it and confirms the rate-limit-config override makes it testable.

- [ ] **Step 1: Write failing tests:**

```csharp
[Trait("Category", "Integration")]
public class LockoutTests : IClassFixture<PostgresFixture>
{
    // Use AuthApiFactory with the default high AuthPermitPerMinute so 11 attempts don't hit 429.
    [Fact] public async Task Tenth_failure_locks_and_correct_password_during_window_still_fails() {
        // 10 wrong-password logins -> locked_until set; audit auth.account_locked.
        // 11th attempt (wrong) -> 401 invalid_credentials.
        // A CORRECT password while locked -> still 401 invalid_credentials (AC2, SEC-03 generic).
    }

    [Fact] public async Task Parallel_wrong_passwords_increment_the_counter_atomically() {
        // Fire N concurrent wrong-password logins via Task.WhenAll; assert failed_login_count == N
        // (no lost updates — #5). Choose N < MaxFailedLogins so no lock interferes, e.g. N = 8.
    }
}

[Trait("Category", "Integration")]
public class AccountEndpointsTests : IClassFixture<PostgresFixture>
{
    [Fact] public async Task Change_password_sets_new_hash_and_revokes_existing_sessions() {
        // login -> change-password -> old refresh token now 401; login with the NEW password works.
    }
    [Fact] public async Task Me_returns_the_current_user() { /* GET /me with Bearer -> 200 with role/tenant */ }
    [Fact] public async Task Me_without_a_token_is_401() { /* unauthorized code */ }
    [Fact] public async Task An_expired_access_token_is_rejected() {
        // Issue a token with Expires in the past (use a dedicated issuer/config or a short lifetime),
        // call /me -> 401. Proves ClockSkew=1min doesn't rescue a clearly-expired token. (AC3, #1)
    }
}
```

> For the expired-token test, the cleanest lever is a second `AuthApiFactory` configured with
> `Jwt:AccessTokenMinutes = 0` (issue-then-immediately-expired, beyond the 1-minute skew after a short
> real delay is flaky) — instead issue a token directly with `JsonWebTokenHandler` and
> `Expires = UtcNow - 2min` signed with `factory.Signing`, then call `/me`. This is deterministic.

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** `change-password`, `me`, and any lockout-audit gaps.
- [ ] **Step 4: Run to verify pass** — targeted integration tests PASS; build + guards green.
- [ ] **Step 5: Commit** — `feat(server): add lockout, change-password, and me endpoints`

---

### Task 6: Rate limiting (#22), no-secret-in-logs (#6), audit completeness (AC4)

**Files:**
- Create: `server/AdaVoice.Server.Api/Infrastructure/AuthRateLimit.cs`
- Modify: `Program.cs` (`AddRateLimiter` with a config-driven fixed-window "auth" policy partitioned
  by remote IP; `UseRateLimiter`; `.RequireRateLimiting("auth")` on the auth endpoint group;
  `OnRejected` writes `429 rate_limited` + `Retry-After`)
- Test: `server/AdaVoice.Server.Tests/Auth/RateLimitAndLoggingTests.cs` `[Integration]`

**Rules:**
- Fixed window, `PermitLimit = RateLimit:AuthPermitPerMinute` (config, default 10), `Window = 1 min`,
  `QueueLimit = 0`, partition key = `HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`.
  A comment states: forwarded-header trust (`KnownProxies`/`KnownNetworks`) is Phase-10 deployment
  config; do not read `X-Forwarded-For` here.
- `OnRejected`: set `Retry-After` (window seconds), write the `rate_limited` problem body.
- `#6`: verify no auth secret reaches logs. Because the app uses `ILogger`, the test injects an
  in-memory logger provider (capture all messages) via `ConfigureLogging` in a subclass/hook of the
  factory, runs login + refresh with a known password + captures the issued refresh token, then
  asserts no captured log message contains the password, the raw refresh token, or the access token.

- [ ] **Step 1: Write failing tests:**

```csharp
[Trait("Category", "Integration")]
public class RateLimitAndLoggingTests : IClassFixture<PostgresFixture>
{
    [Fact] public async Task Exceeding_the_auth_window_returns_429_with_retry_after() {
        // AuthApiFactory with AuthPermitPerMinute = 3; fire 5 logins; assert at least one 429 with
        // code rate_limited and a Retry-After header. (#22)
    }

    [Fact] public async Task No_password_or_token_appears_in_logs() {
        // Capturing logger provider; login + refresh; assert no log line contains the password,
        // the raw refresh token, or the access token. (#6)
    }

    [Fact] public async Task All_auth_events_are_audited() {
        // Exercise login-success, login-failure, refresh, reuse (family revoke), lockout, logout,
        // password-change; assert audit_logs contains a row for each canonical action. (AC4)
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** the rate-limit policy + rejection handler; add the capturing-logger hook
  to the factory.
- [ ] **Step 4: Run to verify pass** — targeted integration tests PASS.
- [ ] **Step 5: Commit** — `feat(server): add auth rate limiting and audit-completeness coverage`

---

## Acceptance-criteria trace

| AC (roadmap Phase 2) | Verified by |
|---|---|
| A rotated refresh token, when replayed, gets `invalid_refresh_token` and the whole family stops working | `RefreshTokenTests.Refresh_rotates_and_old_token_replay_revokes_the_family` (Task 4) |
| The 11th failed login within the window returns lockout; a correct password during lockout still fails | `LockoutTests.Tenth_failure_locks_and_correct_password_during_window_still_fails` (Task 5) |
| JWT validates against the ES256 public key and expires at 15 minutes | `AccessTokenTests` (Task 2) + `AccountEndpointsTests.An_expired_access_token_is_rejected` (Task 5) |
| Every login, refresh, lockout, and reuse event appears in `audit_logs` | `RateLimitAndLoggingTests.All_auth_events_are_audited` (Task 6) |

## Final verification (after Task 6)

1. `dotnet build AdaVoice.slnx -c Release` → 0 warnings / 0 errors.
2. `dotnet test AdaVoice.slnx --filter "Category!=Integration"` → existing desktop suite + DB-less
   server tests green (proves the app is unchanged).
3. docker-compose up; `dotnet test server/AdaVoice.Server.Tests` → all server tests green (integration
   included).
4. Confirm `AdaVoice.Core/Audio/Audio.Wasapi/Host/App` `.csproj` diffs are empty (no new refs); no
   `src/AdaVoice.Licensing` created.
5. Confirm `Directory.Packages.props` holds the 2 new versions; no `Version=` attributes in any `.csproj`.
6. Confirm no migration was added and `MigrationGuardTests` (HasPendingModelChanges == false) is green.
7. `RawSqlGuardTests` green — every `IgnoreQueryFilters` / `FromSql`-`FOR UPDATE` site carries a
   `tenant-scan-ok:` marker; no `FromSqlRaw`/`ExecuteSqlRaw` anywhere.
8. `.github/workflows/ci.yml` untouched (new integration tests run in the existing ubuntu job).
9. superpowers:requesting-code-review (whole branch) → then superpowers:verification-before-completion
   against the 4 ACs and the §14 coverage table.
10. Update `handoff.md` (Phase 2 shipped, Phase 3 next); the plan is already committed under
    `docs/superpowers/plans/`.

## Deferred (explicitly NOT built in Phase 2)

- `signing_keys` table use, JWKS endpoint, license tickets, pinned-key rotation (Phase 5).
- Device binding of refresh tokens / `device_activations` (Phase 4).
- Subscription/tenant-suspended login gating (`tenant_suspended` code) — Phase 3 (needs subscription
  state; Phase 2 only checks user status/lockout).
- Admin CRUD, Razor `/admin`, admin-panel `lockedUntil` display (Phases 3/9).
- Serilog server sink wiring, `/healthz`, `ForwardedHeaders`/`KnownProxies` config (Phase 10/11).
- "Force password change on first login" enforcement (a Phase-1 note, not a Phase-2 criterion).
- Argon2id (Later).
```
