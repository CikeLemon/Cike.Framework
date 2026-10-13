namespace Cike.Data.EFCore;

internal class EntityHelper(ISnowflakeIdGenerator _snowflakeIdGenerator, IGuidGenerator _guidGenerator, ICurrentUser _currentUser, ICurrentTenantAccessor currentTenantAccessor) : ISingletonDependency
{
    /// <summary>
    /// 解析当前用户 Id（匿名时落 0/Empty），供 Set-based 路径写更新审计——与 SetUpdateAuditedProperty 的 TryParse 同源。
    /// </summary>
    public TUserId GetUpdateUserId<TUserId>() where TUserId : struct
    {
        if (typeof(TUserId) == typeof(long))
        {
            long.TryParse(_currentUser.Id, out var id);
            return (TUserId)(object)id;
        }
        Guid.TryParse(_currentUser.Id, out var guidId);
        return (TUserId)(object)guidId;
    }

    public virtual void TrySetId(EntityEntry entry)
    {
        var item = entry.Entity;
        if (item is IEntity<long> longIdEntity && longIdEntity.Id == default)
        {
            entry.Property(nameof(longIdEntity.Id)).CurrentValue = _snowflakeIdGenerator.NextId();
        }
        else if (item is IEntity<Guid> guidIdEntity && guidIdEntity.Id == default)
        {
            entry.Property(nameof(guidIdEntity.Id)).CurrentValue = _guidGenerator.Create();
        }

        if (item is IMultiTenant multiTenant)
        {
            multiTenant.TenantId = currentTenantAccessor.GetTenantId();
        }
    }

    public void SetCreateAuditedProperty(EntityEntry entry)
    {
        var item = entry.Entity;
        if (item is IAuditedEntity<long> longAuditedEntity)
        {
            long.TryParse(_currentUser.Id, out var userId);
            if (longAuditedEntity.CreatedAt == default)
            {
                longAuditedEntity.CreatedBy = userId;
                longAuditedEntity.CreatedAt = DateTime.Now;
            }
        }
        else if (item is IAuditedEntity<Guid> guidAuditedEntity)
        {
            Guid.TryParse(_currentUser.Id, out var userId);
            if (guidAuditedEntity.CreatedAt == default)
            {
                guidAuditedEntity.CreatedBy = userId;
                guidAuditedEntity.CreatedAt = DateTime.Now;
            }
        }
        SetUpdateAuditedProperty(entry);
    }

    public void SetUpdateAuditedProperty(EntityEntry entry)
    {
        var item = entry.Entity;
        if (item is IAuditedEntity<long> longUpdateAuditedEntity)
        {
            long.TryParse(_currentUser.Id, out var userId);
            longUpdateAuditedEntity.UpdatedAt = DateTime.Now;
            longUpdateAuditedEntity.UpdatedBy = userId;
        }
        else if (item is IAuditedEntity<Guid> guidUpdateAuditedEntity)
        {
            Guid.TryParse(_currentUser.Id, out var userId);
            guidUpdateAuditedEntity.UpdatedAt = DateTime.Now;
            guidUpdateAuditedEntity.UpdatedBy = userId;
        }
    }

    /// <summary>
    /// 软删除转换：在 SaveChanges 前统一执行（级联状态修正此时已定型）。
    /// 不能在 ChangeTracker 钩子里做——Remove 的级联在钩子触发之后才把 OwnsOne 条目置为 Deleted；
    /// 也不能用 entry.Reload()——Reload 会打散 OwnsOne 值对象的跟踪状态，导致值对象列被写成 NULL。
    /// 转成 Modified 全量更新，ConcurrencyStamp 在保存前已刷新，乐观并发保护不受影响。
    /// </summary>
    public void SetDeleteAuditedProperty(EntityEntry entry)
    {
        if (!(entry.Entity is ISoftDelete softDeleteEntity))
        {
            return;
        }

        entry.State = EntityState.Modified;
        // OwnsOne 值对象条目已被级联置为 Deleted（= 把值对象列写 NULL），恢复 Unchanged 随主体落库
        foreach (var member in entry.Navigations)
        {
            if (member.CurrentValue is { } target && member.Metadata is INavigation { ForeignKey.IsOwnership: true })
            {
                entry.Context.Entry(target).State = EntityState.Unchanged;
            }
        }
        softDeleteEntity.IsDeleted = true;
        SetUpdateAuditedProperty(entry);
    }
}
