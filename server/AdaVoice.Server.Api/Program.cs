// Phase 2, Task 1: the cross-cutting host skeleton — config, correlation id,
// ProblemDetails, and a global exception handler. No auth logic and no endpoints yet
// (those land in Tasks 2-6). The host must be able to boot (and this partial class must be
// bindable by WebApplicationFactory<Program>) without opening a DB connection at startup.
using AdaVoice.Server.Api.Infrastructure;
using AdaVoice.Server.Infrastructure.Auth;
using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

// Auth, rate limiting, and endpoints are added in Tasks 2-6.

app.Run();

public partial class Program
{
}
