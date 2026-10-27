namespace Cike.OpenIddict;

/// <summary>
/// OpenIddict 校验配置：token 校验的最小配置面 + 完全自定义逃生舱口。
/// </summary>
public class CikeOpenIddictValidationOptions
{
    /// <summary>
    /// token 签发方地址（如 https://idp.example.com）。配置后 OpenIddict 经 OIDC 发现端点拉取签名密钥做本地校验。
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>接受的 audience（token 的 aud 必须匹配，否则 401）。</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// claim 键名映射表：校验成功后把签发方 claim 键名重写为表中的目标键名（缺省映射
    /// sub/name/role 等 → CikeClaimTypes 约定，见 <see cref="Authorization.ClaimTypeMappingHandler"/>）。
    /// 在此追加或覆盖（如 {"upn", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"}）。
    /// </summary>
    public Dictionary<string, string> ClaimTypeMapping { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 逃生舱口：对 OpenIddict 原生校验选项做完全自定义（本地签名密钥、introspection、
    /// DisableClaimsMapping 等）。与本类前两个属性叠加执行，本类先应用、本委托后应用。
    /// </summary>
    public Action<OpenIddictValidationOptions>? Configure { get; set; }
}
