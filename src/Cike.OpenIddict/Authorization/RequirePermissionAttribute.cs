namespace Cike.OpenIddict.Authorization;

/// <summary>
/// 端点权限要求特性：标在端点方法上，要求当前用户拥有指定权限 code，否则 403。
/// </summary>
/// <remarks>
/// 实现 <see cref="IAuthorizeData"/>，经 ASP.NET Core 标准授权管线执行；
/// 实际判定由 <see cref="IPermissionChecker"/> 完成。标在端点类上不生效（仅方法级）。
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : Attribute, IAuthorizeData
{
    public RequirePermissionAttribute(string permission)
    {
        Policy = $"{PermissionPolicyProvider.Prefix}{permission}";
    }

    public string? Policy { get; set; }

    public string? Roles { get; set; }

    public string? AuthenticationSchemes { get; set; }
}
