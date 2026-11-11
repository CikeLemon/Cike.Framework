using Cike.MultiTenancy;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cike.AspNetCore.MinimalAPIs;

[DependsOn([typeof(CikeMultiTenancyModule)])]
public class CikeAspNetCoreMinimalApiModule : CikeModule
{
    public override async Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddHttpContextAccessor();
        context.Services.TryAddSingleton<AutoValidationEndpointFilterProvider>();
        context.Services.Configure<GlobalMinimalApiRouteOptions>(options =>
        {
            options.Prefix = "api";
            options.Version = "v1";
        });

        context.Services.AddObjectAccessor<IApplicationBuilder>();
        context.Services.AddObjectAccessor<IEndpointRouteBuilder>();

        var corsDomains = context.Services.GetConfiguration().GetSection("CorsDomains").Get<string[]>();
        corsDomains ??= ["localhost"];
        context.Services.AddCors(options =>
        {
            options.AddPolicy(nameof(CikeAspNetCoreMinimalApiModule), builder =>
            {
                builder.WithOrigins(corsDomains)
                    .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials()
                        .SetPreflightMaxAge(TimeSpan.FromSeconds(2520));
            });
        });

        context.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new LongToStringConverter());
            options.SerializerOptions.Converters.Add(new NullableLongToStringConverter());
        });
    }

    public override async Task InitializeAsync(ApplicationInitializationContext context)
    {
        var endpointRouteBuilder = context.GetEndpointRouteBuilder();
        var app = context.GetApplicationBuilder();
        app.UseMiddleware<BusinessExceptionMiddleware>();
        app.UseCors(nameof(CikeAspNetCoreMinimalApiModule));

        var globalRouteOptions = app.ApplicationServices.GetRequiredService<IOptions<GlobalMinimalApiRouteOptions>>().Value;
        if (globalRouteOptions.EnabledAuthorization)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }
        AddCikeMinimalAPIs(endpointRouteBuilder);
        await base.InitializeAsync(context);
    }

    private IEndpointRouteBuilder AddCikeMinimalAPIs(IEndpointRouteBuilder builder)
    {
        var globalRouteOptions = builder.ServiceProvider.GetRequiredService<IOptions<GlobalMinimalApiRouteOptions>>().Value;
        var cikeModuleContainer = builder.ServiceProvider.GetRequiredService<CikeModuleContainer>();

        var minimalApiServiceTypeList = cikeModuleContainer.CikeModules.Select(x => x.GetType().Assembly).SelectMany(e => e.GetTypes().Where(x => !x.IsAbstract && x.IsClass && typeof(MinimalApiServiceBase).IsAssignableFrom(x)).Select(x => x)).ToList();
        foreach (var item in minimalApiServiceTypeList)
        {
            var serviceName = item.Name;
            var instance = (MinimalApiServiceBase)builder.ServiceProvider.GetRequiredService(item);
            if (!instance.ServiceName.IsNullOrEmpty())
            {
                serviceName = instance.ServiceName;
            }
            serviceName = serviceName.RemovePostFix(StringComparison.CurrentCultureIgnoreCase, instance.RouteOptions.IgnoredUrlSuffixesInServiceNames?.ToArray() ?? globalRouteOptions.IgnoredUrlSuffixesInServiceNames.ToArray());
            serviceName = globalRouteOptions.GetPluralizationName(serviceName);
            var classEndpointFilters = item.GetCustomAttributes<EndpointFilterBaseAttribute>(true).ToList();

            foreach (var methodInfo in GetMethodInfos(item))
            {
                var methodRouteAttr = methodInfo.GetCustomAttribute<MinimalApiRouteAttribute>();
                IEnumerable<string> httpMethods = [];
                var httpMethodPrefixMapDic = instance.RouteOptions.HttpMethodPrefixMapDic ?? globalRouteOptions.HttpMethodPrefixMapDic;
                foreach (var httpMethodPrefixItem in httpMethodPrefixMapDic)
                {
                    if (methodInfo.Name.StartsWith(httpMethodPrefixItem.Key, StringComparison.CurrentCultureIgnoreCase))
                    {
                        httpMethods = [httpMethodPrefixItem.Value];
                    }
                }
                if (methodRouteAttr != null && methodRouteAttr.HttpMethods.Any())
                {
                    httpMethods = methodRouteAttr.HttpMethods;
                }
                if (!httpMethods.Any())
                {
                    httpMethods = ["Post"];
                }

                var idParameter = methodInfo.GetParameters().FirstOrDefault(p =>
                    p.Name!.Equals("id", StringComparison.OrdinalIgnoreCase) &&
                    p.GetCustomAttributes().All(attr => attr is not IBindingSourceMetadata)
                );
                string methodName = methodInfo.Name.RemovePreFix(httpMethodPrefixMapDic.Keys.ToArray()).RemovePostFix("Async");
                if (idParameter is not null)
                {
                    var id = (idParameter.ParameterType.IsGenericType && idParameter.ParameterType.GetGenericTypeDefinition() == typeof(Nullable<>)) || idParameter.HasDefaultValue ? "{id?}" : "{id}";
                    methodName = $"{(methodName.IsNullOrEmpty() ? "" : $"{methodName}/")}{id}";
                }

                var route = $"{instance.RouteOptions.RootUrl ?? globalRouteOptions.RootUrl}/{serviceName}{(methodName.IsNullOrEmpty() ? "" : $"/{methodName}")}";
                if (methodRouteAttr?.Pattern.IsNullOrEmpty() == false)
                {
                    route = methodRouteAttr.Pattern;
                }

                var routeBuilder = builder.MapMethods(route, httpMethods, CreateDelegate(methodInfo, instance));
                if (globalRouteOptions.EnabledAuthorization && instance.RouteOptions.EnabledAuthorization)
                {
                    (instance.RouteOptions.RouteHandlerBuilder ?? globalRouteOptions.RouteHandlerBuilder)?.Invoke(routeBuilder);
                }
                RegisterEndpointFilter(routeBuilder, methodInfo, classEndpointFilters);
            }
        }
        return builder;
    }

    private IEnumerable<MethodInfo> GetMethodInfos(Type type)
    {
        var bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var routeIgnoreAttribute = typeof(NonActionAttribute);

        return type.GetMethods(bindingFlags)
            .Where(methodInfo =>
                (!methodInfo.IsSpecialName || (methodInfo.IsSpecialName && methodInfo.Name.StartsWith("get_"))) &&
                methodInfo.CustomAttributes.All(attr => attr.AttributeType != routeIgnoreAttribute));
    }

    private Delegate CreateDelegate(MethodInfo methodInfo, object targetInstance)
    {
        var type = Expression.GetDelegateType(
            methodInfo.GetParameters().Select(parameterInfo => parameterInfo.ParameterType)
            .Concat(new List<Type>
            {
                methodInfo.ReturnType
            }).ToArray()
        );
        return Delegate.CreateDelegate(type, targetInstance, methodInfo);
    }

    public static void RegisterEndpointFilter(RouteHandlerBuilder routeHandlerBuilder, MethodInfo methodInfo, List<EndpointFilterBaseAttribute> classEndpointFilters)
    {
        var endpointFilterAttributes = methodInfo.GetCustomAttributes<EndpointFilterBaseAttribute>(true).ToList();
        var tempEndpointFilters = classEndpointFilters.Where(attribute => !endpointFilterAttributes.Any(a => a.GetType() == attribute.GetType())).ToList();
        foreach (var attribute in tempEndpointFilters)
        {
            routeHandlerBuilder.WithMetadata(attribute);
        }
        var allEndpointFilters = endpointFilterAttributes.Union(tempEndpointFilters).OrderBy(attribute => attribute.Order).ToList();

        foreach (var endpointFilter in allEndpointFilters)
        {
            routeHandlerBuilder.AddEndpointFilter(async (invocationContext, next) =>
            {
                var endpointFilterProvider = invocationContext.HttpContext.RequestServices.GetService(endpointFilter.ServiceType) as IEndpointFilterProvider;
                ArgumentNullException.ThrowIfNull(endpointFilterProvider);//需要验证特性中的 ServiceType 是否实现了 IEndpointFilterProvider 接口
                return await endpointFilterProvider.HandlerAsync(invocationContext, next);
            });
        }
    }
}
