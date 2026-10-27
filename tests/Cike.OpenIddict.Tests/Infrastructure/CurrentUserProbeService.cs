namespace Cike.OpenIddict.Tests.Infrastructure;

/// <summary>
/// 组 B 探针端点：把 ICurrentUser 投影出来，供测试断言 Claims 对齐结果。
/// </summary>
public class CurrentUserProbeService(ICurrentUser currentUser) : MinimalApiServiceBase
{
    public Task<object> GetWhoami() => Task.FromResult<object>(new
    {
        Id = currentUser.Id,
        UserName = currentUser.UserName,
        Roles = currentUser.Roles,
        TenantId = currentUser.TenantId,
    });
}
