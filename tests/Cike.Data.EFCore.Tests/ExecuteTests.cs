namespace Cike.Data.EFCore.Tests;

/// <summary>
/// Set-based 批量删除验收：ExecuteDeleteAsync——物理删/软删改写、过滤器生效、立即执行。
/// 验收依据 .scratch/repository-set-based-batch/spec.md（批量更新已挂起，不在本期）。
/// </summary>
public class ExecuteTests : IClassFixture<CikeEfCoreTestHost>
{
    private readonly CikeEfCoreTestHost _host;

    public ExecuteTests(CikeEfCoreTestHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task ExecuteDeleteAsync_Predicate_HardDeletes_WhenEntityIsNotSoftDelete()
    {
        var prefix = $"exec-del-{Guid.NewGuid():N}";
        var categories = Enumerable.Range(0, 3)
            .Select(i => new Category { Name = $"{prefix}-{i}" })
            .ToArray();
        await _host.SeedAsync(categories);
        // 干扰项：不匹配谓词的行必须存活
        var survivor = new Category { Name = $"keep-{Guid.NewGuid():N}" };
        await _host.SeedAsync(survivor);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();

        var affected = await repository.ExecuteDeleteAsync(c => c.Name.StartsWith(prefix));

        Assert.Equal(3, affected);
        Assert.Equal(0, await repository.GetCountAsync(c => c.Name.StartsWith(prefix)));
        Assert.Equal(1, await repository.GetCountAsync(c => c.Name == survivor.Name));
    }

    [Fact]
    public async Task ExecuteDeleteAsync_Query_HardDeletes_WhenEntityIsNotSoftDelete()
    {
        var prefix = $"exec-delq-{Guid.NewGuid():N}";
        var categories = Enumerable.Range(0, 2)
            .Select(i => new Category { Name = $"{prefix}-{i}" })
            .ToArray();
        await _host.SeedAsync(categories);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();

        // 透传式：调用方自行组装查询（此处模拟复杂条件，如跨表 join 后投影本实体）
        var query = repository.GetQueryable().Where(c => c.Name.StartsWith(prefix));
        var affected = await repository.ExecuteDeleteAsync(query);

        Assert.Equal(2, affected);
        Assert.Equal(0, await repository.GetCountAsync(c => c.Name.StartsWith(prefix)));
    }

    [Fact]
    public async Task ExecuteDeleteAsync_Predicate_SoftDeletes_WhenEntityIsSoftDelete()
    {
        var title = $"exec-order-{Guid.NewGuid():N}";
        var order = new Order { Title = title };
        await _host.SeedAsync(order);

        try
        {
            _host.CurrentUser.Id = "42";
            using var scope = _host.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();

            var affected = await repository.ExecuteDeleteAsync(o => o.Title == title);

            Assert.Equal(1, affected);
        }
        finally
        {
            _host.CurrentUser.Id = null;
        }

        // 默认软删过滤器开启：查询与计数不可见
        using (var scope2 = _host.CreateScope())
        {
            var repository2 = scope2.ServiceProvider.GetRequiredService<IReadOnlyRepository<Order, long>>();
            Assert.Null(await repository2.FindAsync(order.Id));
            Assert.Equal(0, await repository2.GetCountAsync(o => o.Title == title));
        }

        // 关闭软删过滤器：行仍在，IsDeleted=true，更新审计已刷新为当前用户
        using (var scope2 = _host.CreateScope())
        {
            var dataFilter = scope2.ServiceProvider.GetRequiredService<IDataFilter>();
            using (dataFilter.Disable<ISoftDelete>())
            {
                var dbContext = scope2.ServiceProvider.GetRequiredService<TestDbContext>();
                var row = await dbContext.Orders.FirstAsync(o => o.Id == order.Id);
                Assert.True(row.IsDeleted);
                Assert.NotEqual(default, row.UpdatedAt);
                Assert.Equal(42L, row.UpdatedBy);
            }
        }
    }

    [Fact]
    public async Task ExecuteDeleteAsync_SoftDeletes_GuidAuditedEntity()
    {
        var guidUser = Guid.NewGuid();
        var order = new GuidOrder { Title = $"exec-guid-{Guid.NewGuid():N}" };
        await _host.SeedAsync(order);

        try
        {
            _host.CurrentUser.Id = guidUser.ToString();
            using var scope = _host.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<GuidOrder, Guid>>();

            var affected = await repository.ExecuteDeleteAsync(o => o.Id == order.Id);

            Assert.Equal(1, affected);
        }
        finally
        {
            _host.CurrentUser.Id = null;
        }

        using (var scope = _host.CreateScope())
        {
            var dataFilter = scope.ServiceProvider.GetRequiredService<IDataFilter>();
            using (dataFilter.Disable<ISoftDelete>())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                var row = await dbContext.Set<GuidOrder>().FirstAsync(o => o.Id == order.Id);
                Assert.True(row.IsDeleted);
                Assert.Equal(guidUser, row.UpdatedBy);
            }
        }
    }

    [Fact]
    public async Task ExecuteDeleteAsync_Query_SoftDeletes_WhenEntityIsSoftDelete()
    {
        var title = $"exec-orderq-{Guid.NewGuid():N}";
        var order = new Order { Title = title };
        await _host.SeedAsync(order);

        try
        {
            _host.CurrentUser.Id = "42";
            using var scope = _host.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();

            var query = repository.GetQueryable().Where(o => o.Title == title);
            var affected = await repository.ExecuteDeleteAsync(query);

            Assert.Equal(1, affected);
        }
        finally
        {
            _host.CurrentUser.Id = null;
        }

        // 默认过滤器不可见；关过滤器后行仍在且已软删——透传式与谓词式走同一改写分支
        using (var scope2 = _host.CreateScope())
        {
            var repository2 = scope2.ServiceProvider.GetRequiredService<IReadOnlyRepository<Order, long>>();
            Assert.Null(await repository2.FindAsync(order.Id));
        }
        using (var scope2 = _host.CreateScope())
        {
            var dataFilter = scope2.ServiceProvider.GetRequiredService<IDataFilter>();
            using (dataFilter.Disable<ISoftDelete>())
            {
                var dbContext = scope2.ServiceProvider.GetRequiredService<TestDbContext>();
                var row = await dbContext.Orders.FirstAsync(o => o.Id == order.Id);
                Assert.True(row.IsDeleted);
                Assert.Equal(42L, row.UpdatedBy);
            }
        }
    }

    [Fact]
    public async Task ExecuteDeleteAsync_NoMatch_ReturnsZero()
    {
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();

        var missing = $"none-{Guid.NewGuid():N}";
        var affected = await repository.ExecuteDeleteAsync(c => c.Name == missing);

        Assert.Equal(0, affected);
    }

    [Fact]
    public async Task ExecuteDeleteAsync_MultiTenant_OnlyDeletesCurrentTenantRows()
    {
        var title = $"exec-tenant-{Guid.NewGuid():N}";
        _host.SetTenant(1);
        try
        {
            await _host.SeedAsync(new TenantOrder { Title = title });
            _host.SetTenant(2);
            await _host.SeedAsync(new TenantOrder { Title = title });

            // 租户 2 批量删除：多租户过滤器生效，只影响自己的一行
            using var scope = _host.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TenantOrder, long>>();
            var affected = await repository.ExecuteDeleteAsync(o => o.Title == title);
            Assert.Equal(1, affected);
        }
        finally
        {
            _host.SetTenant(null);
        }

        // 租户 1 的行存活
        _host.SetTenant(1);
        try
        {
            using var scope = _host.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TenantOrder, long>>();
            Assert.Equal(1, await repository.GetCountAsync(o => o.Title == title));
        }
        finally
        {
            _host.SetTenant(null);
        }
    }

    [Fact]
    public async Task ExecuteDeleteAsync_SoftDeleteParent_ChildRowsRemain()
    {
        var order = new Order { Title = $"exec-parent-{Guid.NewGuid():N}" };
        await _host.SeedAsync(order);
        var lines = Enumerable.Range(1, 2)
            .Select(i => new OrderLine { OrderId = order.Id, ProductName = $"line-{i}", Quantity = 1 })
            .ToArray();
        await _host.SeedAsync(lines);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
        await repository.ExecuteDeleteAsync(o => o.Id == order.Id);

        // 软删改写是 UPDATE 不删行：子表行不受影响（客户端级联本就不触发，删除只可能来自数据库端级联）
        var lineRepository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<OrderLine, long>>();
        Assert.Equal(2, await lineRepository.GetCountAsync(l => l.OrderId == order.Id));
    }

    [Fact]
    public async Task ExecuteDeleteAsync_HardDeleteParent_ReliesOnDatabaseCascade()
    {
        var category = new Category { Name = $"exec-cascade-{Guid.NewGuid():N}" };
        await _host.SeedAsync(category);
        var product = new Product { Name = $"exec-cascade-p-{Guid.NewGuid():N}", CategoryId = category.Id };
        await _host.SeedAsync(product);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();
        await repository.ExecuteDeleteAsync(c => c.Id == category.Id);

        // 非软删父实体走物理 DELETE：子行删除取决于 EF 生成的数据库端外键级联（必选 FK 默认 CASCADE），客户端级联不参与
        var productRepository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Product, long>>();
        Assert.Equal(0, await productRepository.GetCountAsync(p => p.CategoryId == category.Id));
    }

    [Fact]
    public async Task ExecuteDeleteAsync_DoesNotEnqueueDomainEvents()
    {
        var order = new Order { Title = $"exec-event-{Guid.NewGuid():N}" };
        await _host.SeedAsync(order);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
        var loaded = await repository.GetAsync(order.Id);
        loaded.AddDomainEvent(new OrderDeletedEvent());

        // 聚合根带着未发布事件，但 Set-based 删除不经 ChangeTracker：事件不入队（文档化的已知例外）
        await repository.ExecuteDeleteAsync(o => o.Id == order.Id);

        var queueEventBus = scope.ServiceProvider.GetRequiredService<IQueueEventBus>();
        Assert.False(await queueEventBus.AnyQueueAsync());
    }

    private record OrderDeletedEvent : DomainEvent;
}
