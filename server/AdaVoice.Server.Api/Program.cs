// Phase 2: the cross-cutting host skeleton (Task 1) — config, correlation id, ProblemDetails,
// and a global exception handler — plus ES256 JWT issuance/validation wiring (Task 2). No
// endpoints yet (those land in Tasks 3-6). The host must be able to boot (and this partial
// class must be bindable by WebApplicationFactory<Program>) without opening a DB connection at
// startup.
using AdaVoice.Server.Api.Auth;
using AdaVoice.Server.Api.Infrastructure;
using AdaVoice.Server.Infrastructure.Auth;
using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

// Request-scoped tenant context. Real tenant resolution (from the authenticated principal)
// lands with the auth middleware in a later task; for now this keeps the DbContext's
// constructor dependency satisfiable without opening the tenant question early.
builder.Services.AddScoped<ITenantProvider, AmbientTenantProvider>();

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

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// Rate limiting and endpoints are added in Tasks 3-6.

app.Run();

public partial class Program
{
}
