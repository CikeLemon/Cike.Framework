using Cike.Security;

namespace CQRS.Tests.Infrastructure;

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
