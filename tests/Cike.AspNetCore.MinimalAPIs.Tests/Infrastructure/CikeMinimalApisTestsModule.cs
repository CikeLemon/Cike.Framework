namespace Cike.AspNetCore.MinimalAPIs.Tests.Infrastructure;

/// <summary>
/// 测试启动模块：依赖 CikeAspNetCoreMinimalApiModule（约定式自动路由管线），
/// 关闭全局认证开关——聚焦端点映射行为本身，免配认证方案。
/// </summary>
[DependsOn(typeof(CikeAspNetCoreMinimalApiModule))]
public class CikeMinimalApisTestsModule : CikeModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.Configure<GlobalMinimalApiRouteOptions>(o => o.EnabledAuthorization = false);
        return base.ConfigureServicesAsync(context);
    }
}
