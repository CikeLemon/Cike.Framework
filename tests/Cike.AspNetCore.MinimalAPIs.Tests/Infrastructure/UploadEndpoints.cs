namespace Cike.AspNetCore.MinimalAPIs.Tests.Infrastructure;

/// <summary>方法级 [DisableAntiforgery]：仅该端点豁免 antiforgery 要求。</summary>
public class MethodDisabledUploadService : MinimalApiServiceBase
{
    [MinimalApiRoute("upload/method-disabled", "POST")]
    [DisableAntiforgery]
    public Task<IResult> UploadAsync(IFormFile file)
        => Task.FromResult(Results.Ok(new { file.FileName, file.Length }));
}

/// <summary>类级 [DisableAntiforgery]：该服务全部端点豁免 antiforgery 要求。</summary>
[DisableAntiforgery]
public class ClassDisabledUploadService : MinimalApiServiceBase
{
    [MinimalApiRoute("upload/class-disabled", "POST")]
    public Task<IResult> UploadAsync(IFormFile file)
        => Task.FromResult(Results.Ok(new { file.FileName, file.Length }));
}

/// <summary>
/// 基线：无特性的表单端点。.NET 8 起平台自动要求其经过 antiforgery 中间件，
/// 管道未挂 UseAntiforgery() 时请求必 500——有意保留并断言该行为，
/// 证明测试确实能捕获用户报告的症状（防止前两个用例假绿）。
/// </summary>
public class BaselineUploadService : MinimalApiServiceBase
{
    [MinimalApiRoute("upload/baseline", "POST")]
    public Task<IResult> UploadAsync(IFormFile file)
        => Task.FromResult(Results.Ok(new { file.FileName, file.Length }));
}
