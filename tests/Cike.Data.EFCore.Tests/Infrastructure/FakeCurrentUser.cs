using Cike.Security;

namespace Cike.Data.EFCore.Tests.Infrastructure;

/// <summary>
/// 测试用当前用户替身：属性可写，各测试可自行指定 Id / TenantId。
/// 由测试模块以 Replace 方式接管 ICurrentUser 注册。
/// </summary>
public class FakeCurrentUser : ICurrentUser
{
    public string? Id { get; set; }
    public string? UserName { get; set; }
    public string? Name { get; set; }
    public string? NickName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public long? TenantId { get; set; }
    public string[] Roles { get; set; } = [];
    public bool IsAuthorization { get; set; }
}
