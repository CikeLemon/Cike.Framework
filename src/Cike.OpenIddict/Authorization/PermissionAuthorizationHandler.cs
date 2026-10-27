namespace Cike.OpenIddict.Authorization;

/// <summary>
/// 权限授权处理器：调用 <see cref="IPermissionChecker"/> 判定当前用户是否拥有所需权限。
/// 判定为 false 时不做 Succeed，授权整体失败 → 403。
/// </summary>
public class PermissionAuthorizationHandler(IPermissionChecker permissionChecker) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await permissionChecker.IsGrantedAsync(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
