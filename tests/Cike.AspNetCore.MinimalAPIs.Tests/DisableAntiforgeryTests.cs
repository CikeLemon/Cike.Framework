namespace Cike.AspNetCore.MinimalAPIs.Tests;

/// <summary>
/// [DisableAntiforgery] 回归测试。
/// 背景：.NET 8 起带 IFormFile 参数的 Minimal API 端点自动要求 antiforgery 中间件，
/// Cike 管道不挂 UseAntiforgery()，此类端点请求时 500（InvalidOperationException：
/// "contains anti-forgery metadata, but a middleware was not found"）。
/// MVC 的 [IgnoreAntiforgeryToken] 不实现 IAntiforgeryMetadata，对 Minimal API 无效。
/// 框架以自定义 [DisableAntiforgery]（方法级/类级）在映射时调用 .DisableAntiforgery() 解除要求。
/// </summary>
public class DisableAntiforgeryTests : IClassFixture<MinimalApiTestWebHost>
{
    private readonly MinimalApiTestWebHost _host;

    public DisableAntiforgeryTests(MinimalApiTestWebHost host) => _host = host;

    [Fact]
    public async Task MethodLevel_DisableAntiforgery_UploadSucceeds()
    {
        using var client = new HttpClient();
        var (response, body) = await MinimalApiTestWebHost.PostFileAsync(client, $"{_host.BaseUrl}/upload/method-disabled");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("test.txt", body);
    }

    [Fact]
    public async Task ClassLevel_DisableAntiforgery_UploadSucceeds()
    {
        using var client = new HttpClient();
        var (response, body) = await MinimalApiTestWebHost.PostFileAsync(client, $"{_host.BaseUrl}/upload/class-disabled");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("test.txt", body);
    }

    [Fact]
    public async Task NoAttribute_FormEndpoint_StillRequiresAntiforgeryMiddleware()
    {
        // 基线守护：无特性的表单端点保持平台行为（500 + antiforgery 报错）。
        // 此断言若变绿（200），说明宿主管道发生了变化（如挂上了 UseAntiforgery），
        // 需重新审视前两个用例的判别力。
        using var client = new HttpClient();
        var (response, body) = await MinimalApiTestWebHost.PostFileAsync(client, $"{_host.BaseUrl}/upload/baseline");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("anti-forgery metadata", body, StringComparison.OrdinalIgnoreCase);
    }
}
