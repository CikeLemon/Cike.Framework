namespace Cike.MultiTenancy;

public static class CikeTenantConsts
{
    public const string TenantIdKey = "tenant_id";

    public static long GetLongValue(this ClaimsPrincipal claimsPrincipal, string key)
    {
        var str = claimsPrincipal.Claims.Where(c => c.Type == key).FirstOrDefault()?.Value;
        if (long.TryParse(str, out long result))
        {
            return result;
        }
        return 0;
    }
}
