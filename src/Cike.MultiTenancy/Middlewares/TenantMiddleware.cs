namespace Cike.MultiTenancy.Middlewares;

public class TenantMiddleware(ICurrentTenant _currentTenant) : IMiddleware, ITransientDependency
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var tenantId = GetTenantId(context);
        if (tenantId != _currentTenant.Id)
        {
            using (_currentTenant.Change(tenantId))
            {
                await next(context);
            }
        }
        else
        {
            await next(context);
        }
    }

    private long GetTenantId(HttpContext context)
    {
        var tenantId = context.User.GetLongValue(CikeTenantConsts.TenantIdKey);
        if (tenantId > 0)
        {
            return tenantId;
        }

        if (context.Request.Headers.TryGetValue(CikeTenantConsts.TenantIdKey, out var tenantStrval)
            && long.TryParse(tenantStrval.ToString(), out var headerTenantId))
        {
            return headerTenantId;
        }

        if (context.Request.Cookies.TryGetValue(CikeTenantConsts.TenantIdKey, out var tenantStr)
            && long.TryParse(tenantStr.ToString(), out var cookieTenantId))
        {
            return cookieTenantId;
        }

        if (context.Request.Query.TryGetValue(CikeTenantConsts.TenantIdKey, out tenantStrval)
            && long.TryParse(tenantStrval.ToString(), out var queryStringTenantId))
        {
            return queryStringTenantId;
        }

        return 0;
    }
}
