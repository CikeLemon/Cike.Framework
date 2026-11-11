namespace Cike.Data.EFCore;

[DependsOn([
    typeof(CikeSecurityModule),
    typeof(CikeMultiTenancyModule),
    typeof(CikeDomainModule),
    typeof(CikeUniversalIdModule),
    typeof(CikeUowModule),
    typeof(CikeEventBusAdaptiveModule),
])]
public class CikeDataEFCoreModule : CikeModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.Configure<CikeSnowflakeOptions>(options =>
        {
            options.IsEnable = true;
        });
        return base.ConfigureServicesAsync(context);
    }
}
