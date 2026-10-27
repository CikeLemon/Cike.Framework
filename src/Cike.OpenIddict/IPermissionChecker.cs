namespace Cike.OpenIddict;

/// <summary>
/// 权限判定器：授权管线拿端点上声明的权限 code 调用本接口，以返回值决定放行或 403。
/// </summary>
/// <remarks>
/// 判定逻辑（权限数据从哪来、怎么比对）由框架后续补全，业务项目不做实现。
/// </remarks>
public interface IPermissionChecker
{
    /// <summary>判断当前用户是否拥有指定权限。</summary>
    /// <param name="permission">权限 code，即端点特性上声明的值（如 "Order.Create"）。</param>
    /// <returns>true 拥有（放行）；false 不拥有（403）。</returns>
    Task<bool> IsGrantedAsync(string permission);
}
