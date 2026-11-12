namespace Cike.Data.Domain.Entities;

public class FullAuditedEntity<TKey, TUserId> : AuditedEntity<TKey, TUserId>, IFullAuditedEntity<TUserId> where TUserId : struct
{
    public bool IsDeleted { get; set; } = false;
}

public class FullAuditedEntity<TKey> : FullAuditedEntity<TKey, long>
{
}