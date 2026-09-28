namespace Cike.Data.EFCore.Repositories;

/// <summary>
/// EF Core 默认仓储实现。不实现任何 DI 标记接口——默认仓储由 AddCikeDbContext 显式注册，
/// 自定义仓储由业务方继承本类并标注 IScopedDependency 走约定注册。
/// </summary>
public class EfCoreRepository<TDbContext, TEntity, TKey>(TDbContext dbContext) : IRepository<TEntity, TKey>
    where TDbContext : CikeDbContext<TDbContext>
    where TEntity : class, IEntity<TKey>
{
    protected bool asNoTracking = false;

    public TDbContext DbContext { get; } = dbContext;

    public virtual IDisposable BeginAsNoTracking()
    {
        asNoTracking = true;
        return new DisposeAction(() => asNoTracking = false);
    }

    public virtual async Task<TEntity> GetAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(id, cancellationToken);
        if (entity == null)
        {
            throw new UserFriendlyException($"Id {id} is NotFound.");
        }
        return entity;
    }

    public virtual async Task<TEntity?> FindAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return await GetQueryable().FirstOrDefaultAsync(e => e.Id!.Equals(id), cancellationToken);
    }

    public virtual async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
    {
        return await ToListAsync(GetQueryable(), cancellationToken);
    }

    public virtual async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, string sorting = "Id asc", CancellationToken cancellationToken = default)
    {
        return await ToListAsync(GetQueryable().Where(predicate).OrderBy(sorting), cancellationToken);
    }

    public virtual async Task<(long Total, List<TEntity> Items)> GetPagedListAsync(IPagedAndSortedRequest request, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> query = GetQueryable();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        return await ToPagedListAsync(query, request, cancellationToken);
    }

    public virtual async Task<List<TEntity>> ToListAsync(IQueryable<TEntity> query, CancellationToken cancellationToken = default)
    {
        return await query.ToListAsync(cancellationToken);
    }

    public virtual async Task<(long Total, List<TEntity> Items)> ToPagedListAsync(IQueryable<TEntity> query, IPagedAndSortedRequest request, CancellationToken cancellationToken = default)
    {
        return await query.ToPaginationAsync(request, cancellationToken);
    }

    public virtual async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return await GetQueryable().LongCountAsync(cancellationToken);
    }

    public virtual async Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await GetQueryable().LongCountAsync(predicate, cancellationToken);
    }

    public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        if (predicate == null)
        {
            return await GetQueryable().AnyAsync(cancellationToken);
        }
        return await GetQueryable().AnyAsync(predicate, cancellationToken);
    }

    public virtual IQueryable<TEntity> GetQueryable()
    {
        return DbContext.Set<TEntity>().AsNoTracking(asNoTracking);
    }

    public virtual IQueryable<TEntity> WithDetails(params Expression<Func<TEntity, object?>>[] propertyPaths)
    {
        IQueryable<TEntity> query = GetQueryable();
        foreach (var propertyPath in propertyPaths)
        {
            query = query.Include(propertyPath);
        }
        return query;
    }

    public virtual async Task<TEntity> InsertAsync(TEntity entity, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        await InsertManyAsync(new[] { entity }, autoSave, cancellationToken);
        return entity;
    }

    public virtual async Task InsertManyAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        await DbContext.Set<TEntity>().AddRangeAsync(entities, cancellationToken);
        await SaveChangesIfAsync(autoSave, cancellationToken);
    }

    public virtual async Task<TEntity> UpdateAsync(TEntity entity, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        await UpdateManyAsync(new[] { entity }, autoSave, cancellationToken);
        return entity;
    }

    public virtual async Task UpdateManyAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        DbContext.Set<TEntity>().UpdateRange(entities);
        await SaveChangesIfAsync(autoSave, cancellationToken);
    }

    public virtual async Task DeleteAsync(TEntity entity, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        // 软删转换由 CikeDbContext 的 ChangeTracker 钩子完成，仓储只负责 Remove
        await DeleteManyAsync(new[] { entity }, autoSave, cancellationToken);
    }

    public virtual async Task DeleteAsync(TKey id, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(id, cancellationToken);
        if (entity == null)
        {
            return;
        }
        await DeleteAsync(entity, autoSave, cancellationToken);
    }

    public virtual async Task DeleteManyAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        DbContext.Set<TEntity>().RemoveRange(entities);
        await SaveChangesIfAsync(autoSave, cancellationToken);
    }

    protected virtual async Task SaveChangesIfAsync(bool autoSave, CancellationToken cancellationToken = default)
    {
        if (autoSave)
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
