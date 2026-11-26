namespace Cike.AspNetCore.MinimalAPIs.Tests.Infrastructure;

/// <summary>
/// 测试宿主基座：以与生产一致的引导方式（AddApplicationAsync + InitializeApplicationAsync）
/// 在随机端口启动真实 WebApplication（Kestrel）。每个测试类一个实例（IClassFixture）。
/// 环境固定 Development：未处理异常经 DeveloperExceptionPage 返回 500 + 异常文本响应体，便于精确断言。
/// </summary>
public sealed class MinimalApiTestWebHost : IAsyncLifetime
{
    private WebApplication _app = default!;

    /// <summary>宿主监听地址（http://127.0.0.1:随机端口）。</summary>
    public string BaseUrl { get; private set; } = default!;

    public IServiceProvider Services => _app.Services;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await WebApplicationBuilderExtensions.AddApplicationAsync<CikeMinimalApisTestsModule>(builder);

        _app = builder.Build();
        await _app.InitializeApplicationAsync();
        await _app.StartAsync();
        BaseUrl = _app.Urls.First();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>发送一个带单文件字段的 multipart/form-data POST。</summary>
    public static async Task<(HttpResponseMessage Response, string Body)> PostFileAsync(
        HttpClient client, string url, string fieldName = "file", string fileName = "test.txt", string content = "hello")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), fieldName, fileName);
        var response = await client.PostAsync(url, form);
        var body = await response.Content.ReadAsStringAsync();
        return (response, body);
    }
}
