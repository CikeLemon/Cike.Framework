namespace Cike.OpenIddict.Tests.Infrastructure;

/// <summary>
/// 组 A 探针端点：无特性（默认仅登录）/ 单权限 / 双权限 / 角色 / 免认证各一个。
/// </summary>
public class AuthProbeService : MinimalApiServiceBase
{
    public Task<string> GetOpen() => Task.FromResult("open");

    [RequirePermission("Order.Create")]
    public Task<string> GetByPermission() => Task.FromResult("perm");

    [RequirePermission("Order.Create")]
    [RequirePermission("Order.Delete")]
    public Task<string> GetByTwoPermissions() => Task.FromResult("two");

    [Authorize(Roles = "admin")]
    public Task<string> GetByAdminRole() => Task.FromResult("admin");

    [RequirePermission("Order.Create")]
    [Authorize(Roles = "admin")]
    public Task<string> GetByPermissionAndRole() => Task.FromResult("both");
}

/// <summary>端点级认证开关关闭的服务（全局开关开的前提下，本服务全部端点免认证）。</summary>
public class AnonymousProbeService : MinimalApiServiceBase
{
    public AnonymousProbeService()
    {
        RouteOptions.EnabledAuthorization = false;
    }

    [RequirePermission("Order.Create")]
    public Task<string> GetFree() => Task.FromResult("free");
}
