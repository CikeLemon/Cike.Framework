namespace Cike.Security;

public class CikeClaimTypes
{
    /// <summary>
    /// </summary>
    public static string UserName { get; set; } = "username";

    /// <summary>
    /// </summary>
    public static string Name { get; set; } = "name";

    /// <summary>
    /// </summary>
    public static string NickName { get; set; } = "nickname";

    /// <summary>
    /// </summary>
    public static string UserId { get; set; } = "sub";

    /// <summary>
    /// </summary>
    public static string Role { get; set; } = "role";

    /// <summary>
    /// </summary>
    public static string Email { get; set; } = "email";
    /// <summary>
    /// Default: "phone_number".
    /// </summary>
    public static string PhoneNumber { get; set; } = "phone_number";
    /// <summary>
    /// Default: "tenantid".
    /// </summary>
    public static string TenantId { get; set; } = "tenant_id";
}
