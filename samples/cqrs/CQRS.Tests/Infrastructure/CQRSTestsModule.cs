using Cike.Core.Extensions;
using Cike.Core.Modularity;
using Cike.EventBus.Local;
using Cike.Security;
using CQRS.Application;
using CQRS.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CQRS.Tests.Infrastructure;

[DependsOn([typeof(CQRSApplicationModule), typeof(CQRSEntityFrameworkCoreModule), typeof(CikeEventBusLocalModule)])]
public class CQRSTestsModule : CikeModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<FakeCurrentUser>();
        context.Services.Replace(ServiceDescriptor.Singleton<ICurrentUser>(sp => sp.GetRequiredService<FakeCurrentUser>()));
        return base.ConfigureServicesAsync(context);
    }
}
