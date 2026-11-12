using Cike.Core.ObjectAccessor;
using Cike.Core.Threading;
using Cike.MultiTenancy.Middlewares;
using Microsoft.Extensions.Hosting;

namespace Cike.AspNetCore.MinimalAPIs.Extensions;

public static class WebApplicationExtensions
{
    public async static Task InitializeApplicationAsync(this WebApplication app)
    {
        app.Services.GetRequiredService<ObjectAccessor<IApplicationBuilder>>().Value = app;
        app.Services.GetRequiredService<ObjectAccessor<IEndpointRouteBuilder>>().Value = app;
        var application = app.Services.GetRequiredService<IApplicationWithExternalServiceProvider>();
        var applicationLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

        applicationLifetime.ApplicationStopping.Register(() =>
        {
            AsyncHelper.RunSync(() => application.ShutdownAsync(app.Services));
        });

        await application.InitializeAsync(app.Services);
    }

    public static void UseMultiTenant(this IApplicationBuilder app)
    {
        app.UseMiddleware<TenantMiddleware>();
    }
}