namespace Cike.Data.Domain.Repositories;

/// <summary>
/// 仓储完整接口：读（IReadOnlyRepository）+ 写（IBasicRepository）的合并标记接口。
/// 注入本接口即获得全部能力；CQRS 查询侧建议只注入 IReadOnlyRepository 实现编译期只读。
/// </summary>
public interface IRepository<TEntity, TKey> : IBasicRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
{
}
