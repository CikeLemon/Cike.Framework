namespace Cike.OpenIddict.Tests.Infrastructure;

/// <summary>
/// 组 A（授权管线）测试宿主：测试认证方案伪造已登录用户（不走真 token），
/// IPermissionChecker 换成可控测试桩。全程进程内 TestServer，唯一接缝是 HTTP。
/// </summary>
public class AuthorizationTestHost : IAsyncDisposable
{
    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>当前伪造的已登录用户 Claims（请求前设置，默认 nameidentifier=user1）。</summary>
    public static Dictionary<string, string> PrincipalClaims { get; set; } = new()
    {
        [ClaimTypes.NameIdentifier] = "user1",
    };

    /// <summary>是否伪造已认证用户（false 时测试方案返回 NoResult → 未认证）。</summary>
    public static bool Authenticated { get; set; } = true;

    /// <summary>测试桩权限判定结果（默认 false，与框架 stub 语义一致）。</summary>
    public static bool CheckerGrant { get; set; }

    /// <summary>测试桩收到的最近一次权限 code（用于断言管线传参正确）。</summary>
    public static string? LastCheckedPermission { get; set; }

    public static async Task<AuthorizationTestHost> CreateAsync(bool globalAuthorizationEnabled = true)
    {
        var host = new AuthorizationTestHost();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        await builder.Services.AddApplicationAsync<CikeOpenIddictTestsModule>();

        // 组 A 专用：默认认证方案换成测试方案；权限判定换成可控测试桩
        host.ConfigureServices(builder.Services);

        if (!globalAuthorizationEnabled)
        {
            builder.Services.Configure<GlobalMinimalApiRouteOptions>(o => o.EnabledAuthorization = false);
        }

        var app = builder.Build();
        await app.InitializeApplicationAsync();
        await app.StartAsync();

        host._app = app;
        host.Client = app.GetTestClient();
        return host;
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = TestAuthHandler.SchemeName;
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
        }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

        // OpenIddict 启动期即校验配置（无 issuer 直接抛异常）；组 A 不用校验方案，
        // 给一个 .invalid 占位 issuer 让启动通过，该方案全程不会被触发。
        services.Configure<CikeOpenIddictValidationOptions>(o => o.Issuer = "https://cike-tests.invalid/");

        services.RemoveAll<IPermissionChecker>();
        services.AddSingleton<IPermissionChecker>(new TestPermissionChecker(
            () => CheckerGrant,
            permission => LastCheckedPermission = permission));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
