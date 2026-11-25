using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text;

namespace Cike.AspNetCore.Swagger;

public class EnumDescriptionSupplementSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum || schema is not OpenApiSchema openApiSchema)
        {
            return;
        }

        // 数值映射只对数值枚举有意义；若该枚举挂了 JsonStringEnumConverter（schema 是 string），跳过
        if (openApiSchema.Type != JsonSchemaType.Integer)
        {
            return;
        }

        var underlyingType = Enum.GetUnderlyingType(context.Type);
        var mapping = Enum.GetValues(context.Type).Cast<object>()
            .Select(value => $"\"{value}\":{Convert.ChangeType(value, underlyingType)}");

        openApiSchema.Description = string.IsNullOrWhiteSpace(openApiSchema.Description)
            ? $"{{{string.Join(", ", mapping)}}}"
            : $"{openApiSchema.Description}\n{{{string.Join(", ", mapping)}}}";
    }
}
