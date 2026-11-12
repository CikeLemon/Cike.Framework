using Cike.Security;

namespace Cike.Security.Extensions;

public static class ICurrentUserExtensions
{
    public static Guid GetGuidId(this ICurrentUser currentUser)
    {
        return Guid.Parse(currentUser.Id!);
    }

    public static long GetLongId(this ICurrentUser currentUser)
    {
        return long.Parse(currentUser.Id!);
    }
}
