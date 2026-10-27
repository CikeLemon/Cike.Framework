using Microsoft.Extensions.Options;
using OpenIddict.Validation;

namespace Cike.OpenIddict.Authorization;

/// <summary>
/// claim 键名映射处理器：token 校验成功后，把签发方的短名 claim（sub/name/role 等）
/// 重写为 CikeClaimTypes 约定键名，使 ICurrentUser 与角色授权照常工作。
/// 映射表可在 CikeOpenIddictValidationOptions.ClaimTypeMapping 中追加/覆盖。
/// </summary>
public class ClaimTypeMappingHandler(IOptions<CikeOpenIddictValidationOptions> options) : IOpenIddictValidationHandler<ProcessAuthenticationContext>
{
    public ValueTask HandleAsync(ProcessAuthenticationContext context)
    {
        if (context.AccessTokenPrincipal?.Identity is not ClaimsIdentity identity)
        {
            return ValueTask.CompletedTask;
        }

        // 默认映射在执行时求值——CikeClaimTypes 是启动期可改写的静态属性，不能在构造期固化
        var mapping = new Dictionary<string, string>
        {
            ["sub"] = CikeClaimTypes.UserId,
            ["name"] = CikeClaimTypes.UserName,
            ["given_name"] = CikeClaimTypes.Name,
            ["family_name"] = CikeClaimTypes.SurName,
            ["email"] = CikeClaimTypes.Email,
            ["phone_number"] = CikeClaimTypes.PhoneNumber,
            ["role"] = CikeClaimTypes.Role,
        };
        foreach (var pair in options.Value.ClaimTypeMapping)
        {
            mapping[pair.Key] = pair.Value;
        }

        var mappedClaims = identity.Claims.Select(claim =>
        {
            var type = mapping.GetValueOrDefault(claim.Type, claim.Type);
            return type == claim.Type ? claim : new Claim(type, claim.Value, claim.ValueType, claim.Issuer, claim.OriginalIssuer);
        });

        var mappedIdentity = new ClaimsIdentity(
            mappedClaims, identity.AuthenticationType, CikeClaimTypes.UserId, CikeClaimTypes.Role);
        context.AccessTokenPrincipal = new ClaimsPrincipal(mappedIdentity);

        return ValueTask.CompletedTask;
    }
}
