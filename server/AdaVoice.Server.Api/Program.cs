// Phase 2: the cross-cutting host skeleton (Task 1) — config, correlation id, ProblemDetails,
// and a global exception handler — plus ES256 JWT issuance/validation wiring (Task 2). No
// endpoints yet (those land in Tasks 3-6). The host must be able to boot (and this partial
// class must be bindable by WebApplicationFactory<Program>) without opening a DB connection at
// startup.
using AdaVoice.Server.Api.Auth;
using AdaVoice.Server.Api.Infrastructure;
using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Infrastructure.Auth;
using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();

// The connection string is read from configuration first so a test WebApplicationFactory can
// override it via builder.UseSetting(...); falls back to the environment variable of the
// same name for normal (non-test) hosting. AddDbContext registers the context as scoped and
// only opens a connection when a scope actually resolves it — never at startup.
var connectionString = builder.Configuration["ADAVOICE_DB_CONNECTION"]
    ?? Environment.GetEnvironmentVariable("ADAVOICE_DB_CONNECTION")
    ?? throw new InvalidOperationException(
        "ADAVOICE_DB_CONNECTION is not set (configuration key or environment variable).");

builder.Services.AddDbContext<AdaVoiceDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

// Request-scoped tenant context, read from the authenticated principal's tenant_id claim
// (the single trusted source — security-design.md §3). Null on anonymous endpoints
// (login/refresh), which is why the login flow looks users up with a marked filter bypass.
builder.Services.AddScoped<ITenantProvider, HttpContextTenantProvider>();

// Auth application services (orchestration + persistence; no ASP.NET/JOSE types).
builder.Services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// Audit logging: request-scoped writer enqueues onto a singleton bounded channel; a background
// service batch-persists it on a configurable interval (see AuditWriter/AuditFlushService for
// why the write is deferred, not synchronous, and why that is still safe for a security log).
var auditOptions = builder.Configuration.GetSection("Audit").Get<AuditBatchingOptions>()
    ?? new AuditBatchingOptions();
builder.Services.AddSingleton(auditOptions);
builder.Services.AddSingleton<IAuditQueue, AuditQueue>();
builder.Services.AddScoped<IAuditWriter, AuditWriter>();
builder.Services.AddHostedService<AuditFlushService>();

var authPolicy = builder.Configuration.GetSection("Auth").Get<AuthPolicyOptions>()
    ?? new AuthPolicyOptions();
builder.Services.AddSingleton(authPolicy);

builder.Services.AddScoped<CorrelationContext>();
builder.Services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Task 2: ES256 access-token issuance and validation. The signing key is loaded eagerly (same
// fail-fast pattern as the DB connection string above) so a missing ADAVOICE_JWT_SIGNING_KEY
// stops the host at startup instead of on the first authenticated request.
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");
var jwtKeyProvider = new JwtKeyProvider();

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtKeyProvider>(jwtKeyProvider);
builder.Services.AddSingleton<IAccessTokenIssuer, AccessTokenIssuer>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the original JWT claim names ("sub", "tenant_id", "role") instead of the legacy
        // SOAP-era remapping, so HttpContextTenantProvider and the endpoints read them directly.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = jwtKeyProvider.PublicKey,
            ValidAlgorithms = ["ES256"],
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = "role",
            NameClaimType = "sub",
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddAuthRateLimiter(
    builder.Configuration.GetValue("RateLimit:AuthPermitPerMinute", 10));

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

// Rate limiting arrives in Task 6.

await app.RunAsync();

// WebApplicationFactory<Program> binds to this implicit Program type (ASP.NET Core 10 exposes it
// for integration tests without an explicit `public partial class Program`).
