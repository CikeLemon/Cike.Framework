using Cike.Core.DependencyInjection;
using Cike.Data.Domain.Repositories;

namespace Cike.Data.EFCore.Tests;

/// <summary>
/// 工单 6 验收：自定义仓储——继承 EfCoreRepository + 业务接口 + IScopedDependency 约定注册后，
/// 覆盖同实体的默认仓储；领域语义方法可追加；未定制的实体仍用默认仓储。
/// </summary>
public class CustomRepositoryTests : IClassFixture<CikeEfCoreTestHost>
{
    private readonly CikeEfCoreTestHost _host;

    public CustomRepositoryTests(CikeEfCoreTestHost host)
    {
        _host = host;
    }

    [Fact]
    public void Resolve_CustomRepository_CoversDefault()
    {
        using var scope = _host.CreateScope();

        var custom = scope.ServiceProvider.GetRequiredService<ICustomOrderRepository>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
        var readOnlyRepository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Order, long>>();

        Assert.IsType<CustomOrderRepository>(custom);
        Assert.IsType<CustomOrderRepository>(repository);
        Assert.IsType<CustomOrderRepository>(readOnlyRepository);
    }

    [Fact]
    public void Resolve_EntityWithoutCustomRepository_StillUsesDefault()
    {
        using var scope = _host.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();

        Assert.IsType<EfCoreRepository<TestDbContext, Category, long>>(repository);
    }

    [Fact]
    public async Task CustomRepository_AddsDomainSemanticMethod()
    {
        var marker = Guid.NewGuid().ToString("N");
        await _host.SeedAsync(new Order { Title = $"order-{marker}" });

        using var scope = _host.CreateScope();
        var custom = scope.ServiceProvider.GetRequiredService<ICustomOrderRepository>();

        var count = await custom.CountByTitleMarkerAsync(marker);

        Assert.Equal(1, count);
    }
}

/// <summary>自定义仓储的业务接口：仓储能力 + 领域语义方法。</summary>
public interface ICustomOrderRepository : IRepository<Order, long>
{
    Task<long> CountByTitleMarkerAsync(string marker);
}

/// <summary>
/// 自定义仓储：继承 EfCoreRepository 获得全部默认能力，标注 IScopedDependency 走约定注册，
/// 天然覆盖同实体的默认仓储（约定注册先于 AddCikeDbContext 的 TryAdd 默认注册）。
/// 注意：约定注册是程序集级的——本类存在时，同容器其他测试类解析 IRepository&lt;Order, long&gt;
/// 也会得到本类（行为与默认仓储等价，仅追加领域方法）。
/// </summary>
public class CustomOrderRepository : EfCoreRepository<TestDbContext, Order, long>, ICustomOrderRepository, IScopedDependency
{
    public CustomOrderRepository(TestDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<long> CountByTitleMarkerAsync(string marker)
    {
        return await GetCountAsync(o => o.Title.Contains(marker));
    }
}
