using Cike.OpenIddict.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;

namespace Cike.OpenIddict;

/// <summary>
/// OpenIddict 资源服务器模块：注册 token 校验方案（OpenIddict Validation）
/// 与端点权限授权管线（动态策略提供器 + 授权处理器 + 权限判定 stub）。
/// </summary>
[DependsOn([typeof(CikeAuthModule)])]
public class CikeOpenIddictModule : CikeModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        services.AddOpenIddict()
            .AddValidation(builder =>
            {
                builder.UseSystemNetHttp();
                builder.UseAspNetCore();
                builder.AddEventHandler<ProcessAuthenticationContext>(
                    descriptor => descriptor.UseSingletonHandler<ClaimTypeMappingHandler>());
            });

        // issuer/audience 取自 CikeOpenIddictValidationOptions（DI options——宿主模块
        // ConfigureServicesAsync 里 Configure，晚于本模块注册，故经标准 options 管线延迟读取）。
        services.AddTransient<IConfigureOptions<OpenIddictValidationOptions>, ConfigureCikeOpenIddictValidationOptions>();

        // 宿主/测试未显式指定默认认证方案时，落到本模块注册的校验方案上，
        // UseAuthentication 才能把校验产出的 ClaimsPrincipal 写入 HttpContext.User。
        services.AddAuthentication(options =>
        {
            options.DefaultScheme ??= OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme ??= OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        // 授权管线零件：授权核心服务 + 动态策略提供器（替换默认）+ 权限需求处理器 + 判定 stub
        //（stub 手动注册，不走约定注册——将来真正的实现替换这里时保持确定性）。
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, PermissionPolicyProvider.AllowAllHandler>();
        services.AddSingleton<IPermissionChecker, PermissionChecker>();

        return base.ConfigureServicesAsync(context);
    }
}

/// <summary>
/// 把 <see cref="CikeOpenIddictValidationOptions"/> 应用到 OpenIddict 原生校验选项。
/// 未命名 IConfigureOptions 晚于 OpenIddict 自身的命名配置执行，此处设置不会被覆盖。
/// </summary>
public class ConfigureCikeOpenIddictValidationOptions(IOptions<CikeOpenIddictValidationOptions> cikeOptions) : IConfigureOptions<OpenIddictValidationOptions>
{
    public void Configure(OpenIddictValidationOptions options)
    {
        var cike = cikeOptions.Value;
        if (!string.IsNullOrEmpty(cike.Issuer))
        {
            options.Issuer = new Uri(cike.Issuer);
        }
        if (!string.IsNullOrEmpty(cike.Audience))
        {
            options.Audiences.Add(cike.Audience);
        }
        cike.Configure?.Invoke(options);
    }
}
