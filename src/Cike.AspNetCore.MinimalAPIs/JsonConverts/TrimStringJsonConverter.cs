namespace Cike.AspNetCore.MinimalAPIs.JsonConverts;

/// <summary>
/// JSON 反序列化（请求 body 绑定）时自动去除 string 的首尾空白，业务代码无需再手写 .Trim()。
/// <para>
/// 序列化写出侧原样输出；查询串绑定不经过 JSON，不受影响也不应受影响
/// （搜索关键词等查询值合法包含空格）。null 保持 null，纯空白串收敛为空串。
/// </para>
/// <para>
/// 个别需要保留首尾空白的字段（如密码），在属性上标注自定义 <c>[JsonConverter]</c> 覆盖全局行为。
/// </para>
/// </summary>
public sealed class TrimStringJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString()?.Trim();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
