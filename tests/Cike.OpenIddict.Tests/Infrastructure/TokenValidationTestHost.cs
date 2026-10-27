using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using OpenIddict.Abstractions;

namespace Cike.OpenIddict.Tests.Infrastructure;

/// <summary>
/// 组 B（token 校验）测试宿主：OpenIddict 校验配置为本地对称签名密钥 + 静态 OIDC 配置
/// （内嵌 JWKS，不走发现端点、零网络），测试代码用同一密钥签发真 JWT。
/// 验证真实校验链路与 Claims 到 CikeClaimTypes 的对齐。
/// </summary>
public class TokenValidationTestHost : IAsyncDisposable
{
    /// <summary>测试用 RSA 签名密钥（宿主与测试代码共享；OpenIddict access token 签名要求非对称算法）。</summary>
    private static readonly Lazy<RsaSecurityKey> SigningKey = new(() =>
    {
        var rsa = RSA.Create(2048);
        return new RsaSecurityKey(rsa) { KeyId = "cike-test-key" };
    });

    public const string Issuer = "https://cike-tests.local/authority";
    public const string Audience = "order-api";

    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    public static async Task<TokenValidationTestHost> CreateAsync()
    {
        var host = new TokenValidationTestHost();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        await builder.Services.AddApplicationAsync<CikeOpenIddictTestsModule>();

        builder.Services.Configure<CikeOpenIddictValidationOptions>(options =>
        {
            options.Issuer = Issuer;
            options.Audience = Audience;
            options.ClaimTypeMapping["upn"] = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
            options.Configure = openIddict =>
            {
                var credentials = new SigningCredentials(SigningKey.Value, SecurityAlgorithms.RsaSha256);
                openIddict.SigningCredentials.Add(credentials);

                // 静态 OIDC 配置（内嵌 JWKS + 签名密钥）——OpenIddict 拿到 Configuration 后不再发起发现请求
                var configuration = new OpenIddictConfiguration
                {
                    Issuer = new Uri(Issuer),
                    JsonWebKeySet = new JsonWebKeySet
                    {
                        Keys = { JsonWebKeyConverter.ConvertFromSecurityKey(SigningKey.Value) },
                    },
                };
                configuration.SigningKeys.Add(SigningKey.Value);
                openIddict.ConfigurationManager = new StaticConfigurationManager<OpenIddictConfiguration>(configuration);
            };
        });

        var app = builder.Build();
        await app.InitializeApplicationAsync();
        await app.StartAsync();

        host._app = app;
        host.Client = app.GetTestClient();
        return host;
    }

    /// <summary>用共享密钥签发真 JWT。</summary>
    public static string CreateToken(
        Dictionary<string, object>? claims = null,
        string? issuer = Issuer,
        string? audience = Audience,
        DateTime? expires = null)
    {
        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            SigningCredentials = new SigningCredentials(SigningKey.Value, SecurityAlgorithms.RsaSha256),
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            Claims = claims,
            TokenType = "at+jwt",  // OpenIddict 校验 access token 时期待此 typ
        };
        return handler.CreateToken(descriptor);
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
