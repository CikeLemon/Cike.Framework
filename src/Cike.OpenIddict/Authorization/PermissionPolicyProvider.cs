using System.Collections.Concurrent;
using Cike.AspNetCore.MinimalAPIs.Options;
using Microsoft.Extensions.Options;

namespace Cike.OpenIddict.Authorization;

/// <summary>
/// 权限动态策略提供器：把 "Cike:Permission:{code}" 形式的策略名现场解析为授权策略
/// （要求已登录 + <see cref="PermissionRequirement"/>），无需预注册；其余策略名交还默认提供器。
/// 全局认证总开关（GlobalMinimalApiRouteOptions.EnabledAuthorization）关闭时，
/// 权限策略降级为允许模式（开发期一键免认证调试，与"总开关不挂端点默认认证"语义一致）。
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    /// <summary>权限策略名的命名空间前缀，防止与业务自定义策略名冲突。</summary>
    public const string Prefix = "Cike:Permission:";

    private readonly DefaultAuthorizationPolicyProvider _defaultProvider;
    private readonly IOptions<GlobalMinimalApiRouteOptions> _globalRouteOptions;
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policyCache = new();

    public PermissionPolicyProvider(
        IOptions<AuthorizationOptions> authorizationOptions,
        IOptions<GlobalMinimalApiRouteOptions> globalRouteOptions)
    {
        _defaultProvider = new DefaultAuthorizationPolicyProvider(authorizationOptions);
        _globalRouteOptions = globalRouteOptions;
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _defaultProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _defaultProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[Prefix.Length..];
            var allow = !_globalRouteOptions.Value.EnabledAuthorization;
            var policy = _policyCache.GetOrAdd(policyName, _ => new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build());
            return Task.FromResult<AuthorizationPolicy?>(allow ? AllowAllPolicy : policy);
        }

        return _defaultProvider.GetPolicyAsync(policyName);
    }

    /// <summary>恒成功的断言需求——总开关关闭时的允许模式（空策略不合法，用恒 Succeed 的需求实现）。</summary>
    private static readonly AuthorizationPolicy AllowAllPolicy = new AuthorizationPolicyBuilder()
        .AddRequirements(new AllowAllRequirement())
        .Build();

    public class AllowAllRequirement : IAuthorizationRequirement
    {
    }

    public class AllowAllHandler : AuthorizationHandler<AllowAllRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AllowAllRequirement requirement)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
