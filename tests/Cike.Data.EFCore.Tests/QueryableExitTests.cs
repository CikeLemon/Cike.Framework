using Cike.Data.Domain.Repositories;

namespace Cike.Data.EFCore.Tests;

/// <summary>
/// IQueryable 出口验收：GetQueryable 做 Include/复杂查询、BeginAsNoTracking 开关、
/// 全局过滤器（软删/多租户）在出口查询上依然生效。
/// </summary>
public class QueryableExitTests : IClassFixture<CikeEfCoreTestHost>
{
    private readonly CikeEfCoreTestHost _host;

    public QueryableExitTests(CikeEfCoreTestHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task GetQueryable_Include_LoadsNavigationProperties()
    {
        var marker = Guid.NewGuid().ToString("N");
        var order = new Order { Title = $"order-{marker}" };
        order.Lines.Add(new OrderLine { ProductName = $"line-{marker}", Quantity = 2 });
        order.Lines.Add(new OrderLine { ProductName = $"line-{marker}", Quantity = 3 });
        await _host.SeedAsync(order);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Order, long>>();
        var queryable = repository.GetQueryable();

        var loaded = await queryable
            .Include(o => o.Lines)
            .Where(o => o.Title == $"order-{marker}")
            .FirstOrDefaultAsync();

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Lines.Count);
        Assert.All(loaded.Lines, l => Assert.Equal(loaded.Id, l.OrderId));
    }

    [Fact]
    public async Task BeginAsNoTracking_QueriesReturnDetachedEntities()
    {
        var name = $"category-{Guid.NewGuid():N}";
        var category = new Category { Name = name };
        await _host.SeedAsync(category);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // 默认跟踪：查询结果进 ChangeTracker
        await repository.FindAsync(category.Id);
        Assert.Single(dbContext.ChangeTracker.Entries());
        dbContext.ChangeTracker.Clear();

        // 显式关闭跟踪：查询结果不进 ChangeTracker
        using (repository.BeginAsNoTracking())
        {
            await repository.FindAsync(category.Id);
            Assert.Empty(dbContext.ChangeTracker.Entries());
        }

        // 释放后恢复跟踪
        await repository.FindAsync(category.Id);
        Assert.Single(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetQueryable_AppliesSoftDeleteFilter()
    {
        var title = $"order-{Guid.NewGuid():N}";
        var order = new Order { Title = title };
        await _host.SeedAsync(order);
        using (var scope = _host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
            await repository.DeleteAsync(order.Id);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using var verifyScope = _host.CreateScope();
        var verifyRepository = verifyScope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Order, long>>();
        var queryable = verifyRepository.GetQueryable();

        // 软删过滤器在出口 IQueryable 上依然生效：已删实体不可见
        Assert.Equal(0, await queryable.CountAsync(o => o.Id == order.Id));
    }

    [Fact]
    public async Task GetQueryable_AppliesMultiTenantFilter()
    {
        var marker = Guid.NewGuid().ToString("N");
        try
        {
            _host.SetTenant(1);
            await _host.SeedAsync(new TenantOrder { Title = $"order-{marker}-a" });
            _host.SetTenant(2);
            await _host.SeedAsync(new TenantOrder { Title = $"order-{marker}-b" });

            _host.SetTenant(1);
            using var scope = _host.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<TenantOrder, long>>();
            var queryable = repository.GetQueryable();

            var visible = await queryable.Where(o => o.Title.Contains(marker)).ToListAsync();
            Assert.Single(visible);
            Assert.EndsWith("-a", visible[0].Title);
        }
        finally
        {
            _host.SetTenant(null);
        }
    }
}
