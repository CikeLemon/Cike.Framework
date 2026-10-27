namespace Cike.OpenIddict.Authorization;

/// <summary>
/// 权限授权需求：携带端点要求的权限 code，由 <see cref="PermissionAuthorizationHandler"/> 消费。
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    /// <summary>要求的权限 code。</summary>
    public string Permission { get; }
}
