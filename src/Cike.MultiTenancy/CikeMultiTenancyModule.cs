namespace Cike.MultiTenancy;

public class CikeMultiTenancyModule : CikeModule
{
    public override async Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<ICurrentTenantAccessor>(new CurrentTenantAccessor());
        await base.ConfigureServicesAsync(context);
    }
}
