namespace Cike.Domain.Repositories;

/// <summary>
/// 基础仓储：在只读能力之上追加增删改。
/// 写方法均带 autoSave 参数：默认 true 立即保存；配合工作单元时传 false，由事务提交时统一保存。
/// </summary>
public interface IBasicRepository<TEntity, TKey> : IReadOnlyRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
{
    /// <summary>
    /// 插入实体（主键、审计字段、多租户 TenantId 由框架自动填充），返回已填充的实体。
    /// </summary>
    Task<TEntity> InsertAsync(TEntity entity, bool autoSave = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量插入。
    /// </summary>
    Task InsertManyAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新实体（更新审计字段与并发戳由框架自动刷新），返回更新后的实体。
    /// </summary>
    Task<TEntity> UpdateAsync(TEntity entity, bool autoSave = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量更新。
    /// </summary>
    Task UpdateManyAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除实体。ISoftDelete 实体自动转为软删除（IsDeleted=true，后续查询自动过滤）。
    /// </summary>
    Task DeleteAsync(TEntity entity, bool autoSave = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按主键删除；实体不存在时静默返回（幂等）。
    /// </summary>
    Task DeleteAsync(TKey id, bool autoSave = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量删除。
    /// </summary>
    Task DeleteManyAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按条件批量删除（Set-based：一条 SQL 直接执行，不加载实体，立即生效）。
    /// ISoftDelete 实体自动改写为软删除（IsDeleted=true + 更新审计 + 并发戳刷新），与其余删除方法语义一致。
    /// 返回受影响行数（软删时即软删行数）。全局查询过滤器（软删、多租户）继续生效。
    /// 不触发领域事件，不做行级并发校验。
    /// </summary>
    Task<int> ExecuteDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量删除（Set-based）的透传式重载：删除调用方自行组装的查询（含跨表 join / 子查询），语义同谓词式。
    /// </summary>
    Task<int> ExecuteDeleteAsync(IQueryable<TEntity> query, CancellationToken cancellationToken = default);
}
