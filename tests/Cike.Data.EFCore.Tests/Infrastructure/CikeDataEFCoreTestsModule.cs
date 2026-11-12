using Cike.Security;

namespace Cike.Data.EFCore.Tests.Infrastructure;

/// <summary>
/// 测试启动模块：加载与生产一致的模块依赖图（数据层全家桶 + 本地事件总线），
/// 并用 FakeCurrentUser 接管 ICurrentUser，避免依赖 HttpContext。
/// </summary>
[DependsOn([typeof(CikeDataEFCoreModule), typeof(CikeEventBusLocalModule)])]
public class CikeDataEFCoreTestsModule : CikeModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 同实例双注册：ICurrentUser 接口与具体类型解析到同一个 FakeCurrentUser，测试基座无需强转
        context.Services.AddSingleton<FakeCurrentUser>();
        context.Services.Replace(ServiceDescriptor.Singleton<ICurrentUser>(sp => sp.GetRequiredService<FakeCurrentUser>()));
        return base.ConfigureServicesAsync(context);
    }
}
