using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Cike.OpenIddict.Tests;

/// <summary>
/// 组 B：token 校验行为。接缝 = 进程内 HTTP 宿主的最终状态码与 ICurrentUser 投影。
/// 真实 OpenIddict 校验链路（本地对称密钥，无网络依赖）。
/// </summary>
public class TokenValidationTests : IClassFixture<TokenValidationTestHostFixture>
{
    private readonly TokenValidationTestHostFixture _fixture;

    public TokenValidationTests(TokenValidationTestHostFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient Client => _fixture.Host.Client;

    [Fact]
    public async Task ValidToken_ProtectedEndpoint_Returns200()
    {
        var token = TokenValidationTestHost.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "user1",
        });

        var response = await SendWithToken("/api/v1/AuthProbes/Open", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingToken_ProtectedEndpoint_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/AuthProbes/Open");

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task TamperedSignature_Returns401()
    {
        var token = TokenValidationTestHost.CreateToken(new Dictionary<string, object> { ["sub"] = "user1" });
        var tampered = token[..^6] + "xxxxxx";

        var response = await SendWithToken("/api/v1/AuthProbes/Open", tampered);

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task WrongIssuer_Returns401()
    {
        var token = TokenValidationTestHost.CreateToken(
            new Dictionary<string, object> { ["sub"] = "user1" },
            issuer: "https://evil.local/authority");

        var response = await SendWithToken("/api/v1/AuthProbes/Open", token);

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task WrongAudience_Returns401()
    {
        var token = TokenValidationTestHost.CreateToken(
            new Dictionary<string, object> { ["sub"] = "user1" },
            audience: "other-api");

        var response = await SendWithToken("/api/v1/AuthProbes/Open", token);

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var token = TokenValidationTestHost.CreateToken(
            new Dictionary<string, object> { ["sub"] = "user1" },
            expires: DateTime.UtcNow.AddMinutes(-5));

        var response = await SendWithToken("/api/v1/AuthProbes/Open", token);

        AssertUnauthorized(response);
    }

    [Fact]
    public async Task ClaimsAlignedWithCikeClaimTypes_ICurrentUserReadsThem()
    {
        var token = TokenValidationTestHost.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "42",
            ["name"] = "lemon.li",
            ["role"] = "admin",
            ["tenantid"] = "7",
        });

        var response = await SendWithToken("/api/v1/CurrentUserProbes/Whoami", token);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("42", json.GetProperty("id").GetString());
        Assert.Equal("lemon.li", json.GetProperty("userName").GetString());
        Assert.Equal("admin", json.GetProperty("roles")[0].GetString());
        Assert.Equal("7", json.GetProperty("tenantId").GetString()); // LongToStringConverter 把 long 序列化为字符串
    }

    [Fact]
    public async Task ValidToken_WithPermissionAttribute_Returns403BecauseStubDenies()
    {
        // 组 B 与组 A 汇合点：真 token 过了认证，权限判定仍走 stub（恒 false）→ 403
        var token = TokenValidationTestHost.CreateToken(new Dictionary<string, object> { ["sub"] = "user1" });

        var response = await SendWithToken("/api/v1/AuthProbes/ByPermission", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ValidToken_AdminRoleClaim_AuthorizeRolesEndpoint_Returns200()
    {
        // 真 token 的短名 "role" claim 经映射变 ClaimTypes.Role 后，内置角色授权照常工作
        var token = TokenValidationTestHost.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "user1",
            ["role"] = "admin",
        });

        var response = await SendWithToken("/api/v1/AuthProbes/ByAdminRole", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ClaimTypeMappingOverride_CustomClaimMapsToUserId()
    {
        // 宿主 Configure 里把 "upn" 追加映射为 CikeClaimTypes.UserId
        var token = TokenValidationTestHost.CreateToken(new Dictionary<string, object>
        {
            ["upn"] = "99",
        });

        var response = await SendWithToken("/api/v1/CurrentUserProbes/Whoami", token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(body.Contains("\"id\":\"99\""), $"actual body: {body}");
    }

    private Task<HttpResponseMessage> SendWithToken(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.SendAsync(request);
    }

    /// <summary>401 断言附带 WWW-Authenticate 错误详情，失败时一眼看出校验拒绝原因。</summary>
    private static void AssertUnauthorized(HttpResponseMessage response)
    {
        var challenge = response.Headers.WwwAuthenticate.FirstOrDefault();
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected 401, got {(int)response.StatusCode}. WWW-Authenticate: {challenge?.Scheme} {challenge?.Parameter}");
    }
}

public class TokenValidationTestHostFixture : IAsyncLifetime
{
    public TokenValidationTestHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Host = await TokenValidationTestHost.CreateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
