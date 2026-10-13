namespace Cike.Data.EFCore.Tests.Infrastructure;

/// <summary>普通实体：仅主键，无审计、无软删。</summary>
public class Category : Entity<long>
{
    public string Name { get; set; } = default!;
}

/// <summary>审计实体：CreatedAt / CreatedBy / UpdatedAt / UpdatedBy 自动填充。</summary>
public class Product : AuditedEntity<long>
{
    public string Name { get; set; } = default!;

    public long CategoryId { get; set; }
    public Category Category { get; set; } = default!;
}

/// <summary>标准聚合根：审计 + 软删 + 领域事件 + 并发戳。</summary>
public class Order : FullAuditedAggregateRoot<long>
{
    public string Title { get; set; } = default!;

    public List<OrderLine> Lines { get; set; } = new();
}

/// <summary>聚合根子实体：用于导航属性（Include）查询。</summary>
public class OrderLine : Entity<long>
{
    public long OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public string ProductName { get; set; } = default!;
    public int Quantity { get; set; }
}

/// <summary>多租户实体：按当前租户过滤。</summary>
public class TenantOrder : FullAuditedAggregateRoot<long>, IMultiTenant
{
    public string Title { get; set; } = default!;

    public long TenantId { get; set; }
}

/// <summary>Guid 审计用户聚合根：验证审计用户 Id 为 Guid 型的软删改写路径。</summary>
public class GuidOrder : FullAuditedAggregateRoot<Guid, Guid>
{
    public string Title { get; set; } = default!;
}
