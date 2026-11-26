namespace Cike.AspNetCore.MinimalAPIs.Tests.Infrastructure;

/// <summary>回显端点入参：字符串字段绑定后原样返回，用于断言 JSON 读取侧 Trim 行为。</summary>
public class EchoDto
{
    public string? Name { get; set; }
    public string? Note { get; set; }
}

public class EchoService : MinimalApiServiceBase
{
    /// <summary>JSON body 回显：复杂类型走 body 绑定，经过 HttpJsonOptions 的转换器管线。</summary>
    [MinimalApiRoute("echo/body", "POST")]
    public Task<IResult> PostBodyAsync(EchoDto dto)
        => Task.FromResult(Results.Ok(new { name = dto.Name, note = dto.Note }));

    /// <summary>查询串回显：简单类型走查询串绑定，不经过 JSON——查询参数与响应写出都必须原样。</summary>
    [MinimalApiRoute("echo/query", "GET")]
    public Task<IResult> GetQueryAsync(string keyword)
        => Task.FromResult(Results.Ok(new { keyword }));
}
