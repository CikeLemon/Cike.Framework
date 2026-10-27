namespace Cike.OpenIddict.Authorization;

/// <summary>
/// <see cref="IPermissionChecker"/> 的暂存实现：判定逻辑留空，恒返回 false（fail-closed，绝不误放行）。
/// </summary>
public class PermissionChecker : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string permission)
    {
        // TODO: 权限判定逻辑待补充（对接权限服务/缓存等），补全前一律拒绝。
        return Task.FromResult(false);
    }
}
