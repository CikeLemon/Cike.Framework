using Cike.Data.Domain.Repositories;

namespace Cike.Data.Extensions;

public static class IServiceCollectionDbContextExtensions
{
    public static IServiceCollection AddCikeDbContext<TDbContext>(this IServiceCollection services, bool addDefaultRepositories = true) where TDbContext : CikeDbContext<TDbContext>
    {
        services.TryAddTransient(DbContextOptionsFactory.Create<TDbContext>);
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<TDbContext>>();

        if (addDefaultRepositories)
        {
            AddDefaultRepositories<TDbContext>(services);
        }

        return services;
    }

    /// <summary>
    /// 为 DbContext 中每个实现了 IEntity&lt;TKey&gt; 的 DbSet 实体注册默认仓储（Scoped，TryAdd 语义）。
    /// 模块加载的约定注册（自定义仓储）先于本方法执行，因此自定义仓储天然覆盖默认仓储；
    /// 多个 DbContext 含同一实体类型时先注册者保留，需要特定实现请用自定义仓储。
    /// </summary>
    private static void AddDefaultRepositories<TDbContext>(IServiceCollection services) where TDbContext : CikeDbContext<TDbContext>
    {
        foreach (var entityType in GetEntityTypes(typeof(TDbContext)))
        {
            // 领域层静态 EntityHelper（Cike.Domain），与本项目 internal EntityHelper 同名，全限定消歧
            var primaryKeyType = Cike.Domain.EntityHelper.FindPrimaryKeyType(entityType);
            if (primaryKeyType == null)
            {
                continue;
            }

            var implementationType = typeof(EfCoreRepository<,,>).MakeGenericType(typeof(TDbContext), entityType, primaryKeyType);
            services.TryAddScoped(typeof(IRepository<,>).MakeGenericType(entityType, primaryKeyType), implementationType);
            services.TryAddScoped(typeof(IBasicRepository<,>).MakeGenericType(entityType, primaryKeyType), implementationType);
            services.TryAddScoped(typeof(IReadOnlyRepository<,>).MakeGenericType(entityType, primaryKeyType), implementationType);
        }
    }

    public static IEnumerable<Type> GetEntityTypes(Type dbContextType)
    {
        return
            from property in dbContextType.GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            where
                ReflectionHelper.IsAssignableToGenericType(property.PropertyType, typeof(DbSet<>)) &&
                typeof(IEntity).IsAssignableFrom(property.PropertyType.GenericTypeArguments[0])
            select property.PropertyType.GenericTypeArguments[0];
    }
}
