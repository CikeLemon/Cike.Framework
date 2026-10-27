namespace Cike.OpenIddict.Tests;

/// <summary>
/// 组 A：授权管线行为。接缝 = 进程内 HTTP 宿主的最终状态码。
/// 认证由测试方案伪造，权限判定由测试桩控制，只验证外部可见行为。
/// </summary>
public class AuthorizationPipelineTests : IClassFixture<AuthorizationTestHostFixture>
{
    private readonly AuthorizationTestHostFixture _fixture;

    public AuthorizationPipelineTests(AuthorizationTestHostFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient Client => _fixture.Host.Client;

    [Fact]
    public async Task PermissionEndpoint_StubAlwaysDenies_AuthenticatedUser_Returns403()
    {
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.CheckerGrant = false;

        var response = await Client.GetAsync("/api/v1/AuthProbes/ByPermission");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PermissionEndpoint_StubGrants_AuthenticatedUser_Returns200()
    {
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.CheckerGrant = true;

        var response = await Client.GetAsync("/api/v1/AuthProbes/ByPermission");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PermissionEndpoint_PassesDeclaredPermissionCodeToChecker()
    {
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.CheckerGrant = false;
        AuthorizationTestHost.LastCheckedPermission = null;

        await Client.GetAsync("/api/v1/AuthProbes/ByPermission");

        Assert.Equal("Order.Create", AuthorizationTestHost.LastCheckedPermission);
    }

    [Fact]
    public async Task PermissionEndpoint_Unauthenticated_Returns401()
    {
        AuthorizationTestHost.CheckerGrant = false;
        AuthorizationTestHost.Authenticated = false;

        var response = await Client.GetAsync("/api/v1/AuthProbes/ByPermission");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TwoPermissionsEndpoint_Granted_Returns200()
    {
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.CheckerGrant = true;
        AuthorizationTestHost.PrincipalClaims = new() { [ClaimTypes.NameIdentifier] = "user1" };

        var response = await Client.GetAsync("/api/v1/AuthProbes/ByTwoPermissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminRoleEndpoint_AdminRole_Returns200()
    {
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.PrincipalClaims = new()
        {
            [ClaimTypes.NameIdentifier] = "user1",
            [ClaimTypes.Role] = "admin",
        };

        var response = await Client.GetAsync("/api/v1/AuthProbes/ByAdminRole");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminRoleEndpoint_NonAdminRole_Returns403()
    {
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.PrincipalClaims = new()
        {
            [ClaimTypes.NameIdentifier] = "user1",
            [ClaimTypes.Role] = "user",
        };

        var response = await Client.GetAsync("/api/v1/AuthProbes/ByAdminRole");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PermissionPlusRoleEndpoint_GrantedWithAdminRole_Returns200()
    {
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.CheckerGrant = true;
        AuthorizationTestHost.PrincipalClaims = new()
        {
            [ClaimTypes.NameIdentifier] = "user1",
            [ClaimTypes.Role] = "admin",
        };

        var response = await Client.GetAsync("/api/v1/AuthProbes/ByPermissionAndRole");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PermissionPlusRoleEndpoint_GrantedWithoutAdminRole_Returns403()
    {
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.CheckerGrant = true;
        AuthorizationTestHost.PrincipalClaims = new()
        {
            [ClaimTypes.NameIdentifier] = "user1",
            [ClaimTypes.Role] = "user",
        };

        var response = await Client.GetAsync("/api/v1/AuthProbes/ByPermissionAndRole");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EndpointWithoutAttribute_Authenticated_Returns200()
    {
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.PrincipalClaims = new() { [ClaimTypes.NameIdentifier] = "user1" };

        var response = await Client.GetAsync("/api/v1/AuthProbes/Open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EndpointWithoutAttribute_Unauthenticated_Returns401()
    {
        AuthorizationTestHost.Authenticated = false;

        var response = await Client.GetAsync("/api/v1/AuthProbes/Open");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EndpointLevelAuthDisabled_ExplicitPermissionStillEnforced_Returns403()
    {
        // 端点级开关只关掉 MinimalAPIs 默认的"要求登录"；方法上显式声明的权限要求
        // 不被服务级开关绕过（只有全局总开关关闭授权中间件才全部免认证）。
        AuthorizationTestHost.Authenticated = true;
        AuthorizationTestHost.CheckerGrant = false;
        AuthorizationTestHost.PrincipalClaims = new() { [ClaimTypes.NameIdentifier] = "user1" };

        var response = await Client.GetAsync("/api/v1/AnonymousProbes/Free");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

/// <summary>
/// 全局认证总开关关闭的宿主：端点不带默认认证要求；权限特性不强制——
/// 因为 PermissionPolicyProvider 在总开关关闭时降级为允许模式。
/// （注意：认证/授权中间件实际仍被 WebApplication 自动挂载——本包注册了
/// 认证/授权服务；200 的来源是允许模式策略，不是中间件缺席。）
/// </summary>
public class GlobalAuthorizationDisabledTests : IDisposable
{
    private readonly AuthorizationTestHost _host;

    public GlobalAuthorizationDisabledTests()
    {
        _host = AuthorizationTestHost.CreateAsync(globalAuthorizationEnabled: false).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GlobalSwitchOff_UnauthenticatedRequest_Returns200()
    {
        AuthorizationTestHost.Authenticated = false;
        AuthorizationTestHost.PrincipalClaims = new() { [ClaimTypes.NameIdentifier] = "user1" };

        var response = await _host.Client.GetAsync("/api/v1/AuthProbes/Open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GlobalSwitchOff_PermissionAttributeNotEnforced_Returns200()
    {
        AuthorizationTestHost.Authenticated = false;
        AuthorizationTestHost.CheckerGrant = false;

        var response = await _host.Client.GetAsync("/api/v1/AuthProbes/ByPermission");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose() => _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

public class AuthorizationTestHostFixture : IAsyncLifetime
{
    public AuthorizationTestHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Host = await AuthorizationTestHost.CreateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
