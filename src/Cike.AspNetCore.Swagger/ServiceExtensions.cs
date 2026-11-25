using Microsoft.OpenApi;
using System.Reflection.Metadata;

namespace Cike.AspNetCore.Swagger;

public static class ServiceExtensions
{
    public static IServiceCollection AddCikeSwagger(this IServiceCollection services, string projectName, Action<SwaggerGenOptions> configOptions = null)
    {
        return services.AddEndpointsApiExplorer()
            .AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo() { Title = projectName, Version = "v1" });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "输入 JWT（Swagger UI 会自动加 Bearer 前缀）",
                });
                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });

                foreach (var item in Directory.GetFiles(AppContext.BaseDirectory, "*.xml")
                             .Where(x => !Path.GetFileName(x).StartsWith("Microsoft.")))
                {
                    options.IncludeXmlComments(item);
                }

                options.UseAllOfForInheritance();
                options.UseOneOfForPolymorphism();
                options.SchemaFilter<EnumDescriptionSupplementSchemaFilter>();
                configOptions?.Invoke(options);
            });
    }

}
