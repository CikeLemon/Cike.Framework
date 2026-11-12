namespace Cike.Data.Domain.Entities;

public class AuditedEntity<TKey, TUserId> : Entity<TKey>, IAuditedEntity<TUserId> where TUserId : struct
{
    public DateTime CreatedAt { get; set; }

    public TUserId CreatedBy { get; set; } = default!;

    public DateTime UpdatedAt { get; set; }

    public TUserId UpdatedBy { get; set; } = default!;
}

public class AuditedEntity<TKey> : AuditedEntity<TKey, long>, IAuditedEntity<long>
{
}
