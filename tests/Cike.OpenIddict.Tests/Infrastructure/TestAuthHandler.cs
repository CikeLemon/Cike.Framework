namespace Cike.OpenIddict.Tests.Infrastructure;

/// <summary>
/// 测试认证方案：无条件把 <see cref="AuthorizationTestHost.PrincipalClaims"/> 静态Claims
/// 认证为当前用户——组 A 测试不依赖真 token，专注授权管线行为。
/// </summary>
public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthorizationTestHost.Authenticated)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            AuthorizationTestHost.PrincipalClaims.Select(kv => new Claim(kv.Key, kv.Value)),
            authenticationType: SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
