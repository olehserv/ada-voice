using AdaVoice.Server.Infrastructure.Persistence;

namespace AdaVoice.Server.Api.Auth;

/// <summary>Supplies the current tenant from the authenticated principal's <c>tenant_id</c>
/// claim — the single trusted source for tenant scope (security-design.md §3: tenant_id always
/// comes from the validated JWT, never from a request body). Returns null when there is no
/// authenticated user (anonymous endpoints like login/refresh), which makes tenant-filtered
/// queries return no rows — the login flow therefore looks users up with a deliberate,
/// marked filter bypass.</summary>
public sealed class HttpContextTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextTenantProvider(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? CurrentTenantId
    {
        get
        {
            var claim = _accessor.HttpContext?.User.FindFirst("tenant_id")?.Value;
            return Guid.TryParse(claim, out var tenantId) ? tenantId : null;
        }
    }
}
