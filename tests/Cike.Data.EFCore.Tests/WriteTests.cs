using Cike.Data.Domain.Repositories;

namespace Cike.Data.EFCore.Tests;

/// <summary>
/// 工单 4 验收：写入路径与 autoSave——插入（主键/审计自动填充）、更新（更新审计刷新）、
/// 删除（软删/硬删/幂等）、autoSave=false 配合工作单元、领域事件保存后入队。
/// </summary>
public class WriteTests : IClassFixture<CikeEfCoreTestHost>
{
    private readonly CikeEfCoreTestHost _host;

    public WriteTests(CikeEfCoreTestHost host)
    {
        _host = host;
    }

    [Fact]
    public void Resolve_IRepository_And_IBasicRepository_AreEfCoreRepository()
    {
        using var scope = _host.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();
        var basicRepository = scope.ServiceProvider.GetRequiredService<IBasicRepository<Category, long>>();

        Assert.IsType<EfCoreRepository<TestDbContext, Category, long>>(repository);
        Assert.IsType<EfCoreRepository<TestDbContext, Category, long>>(basicRepository);
    }

    [Fact]
    public async Task InsertAsync_AutoGeneratesIdAndAuditFields()
    {
        var name = $"product-{Guid.NewGuid():N}";
        var category = new Category { Name = $"category-{Guid.NewGuid():N}" };
        await _host.SeedAsync(category);
        try
        {
            _host.CurrentUser.Id = "42";
            using var scope = _host.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Product, long>>();

            var product = await repository.InsertAsync(new Product { Name = name, CategoryId = category.Id });

            Assert.NotEqual(0L, product.Id);
            Assert.NotEqual(default, product.CreatedAt);
            Assert.Equal(42L, product.CreatedBy);
            Assert.NotEqual(default, product.UpdatedAt);
            Assert.Equal(42L, product.UpdatedBy);
        }
        finally
        {
            _host.CurrentUser.Id = null;
        }
    }

    [Fact]
    public async Task InsertAsync_AutoSaveFalse_PendsUntilUnitOfWorkCommit()
    {
        var category = new Category { Name = $"category-{Guid.NewGuid():N}" };
        using (var scope = _host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();

            await repository.InsertAsync(category, autoSave: false);

            // 变更仍挂起，未落库
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            Assert.True(dbContext.ChangeTracker.HasChanges());

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        // 提交后跨 Scope 可见
        using (var scope = _host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();
            Assert.NotNull(await repository.FindAsync(category.Id));
        }
    }

    [Fact]
    public async Task InsertManyAsync_PersistsAll()
    {
        var prefix = $"many-{Guid.NewGuid():N}";
        var categories = Enumerable.Range(0, 2)
            .Select(i => new Category { Name = $"{prefix}-{i}" })
            .ToArray();

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();
        await repository.InsertManyAsync(categories);

        var count = await repository.GetCountAsync(c => c.Name.StartsWith(prefix));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesRowAndRefreshesUpdateAudit()
    {
        var category = new Category { Name = $"category-{Guid.NewGuid():N}" };
        await _host.SeedAsync(category);
        var product = new Product { Name = $"product-{Guid.NewGuid():N}", CategoryId = category.Id };
        await _host.SeedAsync(product);
        var newName = $"renamed-{Guid.NewGuid():N}";
        try
        {
            _host.CurrentUser.Id = "42";
            using var scope = _host.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Product, long>>();

            var loaded = await repository.GetAsync(product.Id);
            loaded.Name = newName;
            await repository.UpdateAsync(loaded);
            // autoSave 只负责 SaveChanges；事务提交是工作单元的职责（生产中由请求管道完成）
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }
        finally
        {
            _host.CurrentUser.Id = null;
        }

        using var verifyScope = _host.CreateScope();
        var verifyRepository = verifyScope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Product, long>>();
        var updated = await verifyRepository.GetAsync(product.Id);
        Assert.Equal(newName, updated.Name);
        // 更新审计已刷新（种子阶段当前用户为空 → UpdatedBy=0，更新时为 42）
        Assert.Equal(42L, updated.UpdatedBy);
        Assert.NotEqual(default, updated.UpdatedAt);
    }

    [Fact]
    public async Task UpdateManyAsync_UpdatesAll()
    {
        var prefix = $"updatemany-{Guid.NewGuid():N}";
        var categories = Enumerable.Range(0, 2)
            .Select(i => new Category { Name = $"{prefix}-{i}" })
            .ToArray();
        await _host.SeedAsync(categories);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();
        var loaded = await repository.GetListAsync(c => c.Name.StartsWith(prefix));
        foreach (var category in loaded)
        {
            category.Name = $"{category.Name}-done";
        }
        await repository.UpdateManyAsync(loaded);

        var updated = await repository.GetListAsync(c => c.Name.StartsWith(prefix));
        Assert.Equal(2, updated.Count);
        Assert.All(updated, c => Assert.EndsWith("-done", c.Name));
    }

    [Fact]
    public async Task DeleteAsync_ById_SoftDeletes_WhenEntityIsSoftDelete()
    {
        var title = $"order-{Guid.NewGuid():N}";
        var order = new Order { Title = title };
        await _host.SeedAsync(order);

        using (var scope = _host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
            await repository.DeleteAsync(order.Id);
            // autoSave 只负责 SaveChanges；事务提交是工作单元的职责（生产中由请求管道完成）
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        // 软删过滤器默认开启：查询与计数不可见
        using (var scope = _host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Order, long>>();
            Assert.Null(await repository.FindAsync(order.Id));
            Assert.Equal(0, await repository.GetCountAsync(o => o.Title == title));
        }

        // 关闭软删过滤器：行仍在，IsDeleted=true，删除审计已写
        using (var scope = _host.CreateScope())
        {
            var dataFilter = scope.ServiceProvider.GetRequiredService<IDataFilter>();
            using (dataFilter.Disable<ISoftDelete>())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                var row = await dbContext.Orders.FirstAsync(o => o.Id == order.Id);
                Assert.True(row.IsDeleted);
                Assert.NotEqual(default, row.UpdatedAt);
            }
        }
    }

    [Fact]
    public async Task DeleteAsync_HardDeletes_WhenEntityIsNotSoftDelete()
    {
        var category = new Category { Name = $"category-{Guid.NewGuid():N}" };
        await _host.SeedAsync(category);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();
        await repository.DeleteAsync(category.Id);

        // Category 非软删实体：行物理删除
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Null(await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == category.Id));
    }

    [Fact]
    public async Task DeleteAsync_NotExists_CompletesSilently()
    {
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();

        var exception = await Record.ExceptionAsync(() => repository.DeleteAsync(-1));

        Assert.Null(exception);
    }

    [Fact]
    public async Task DeleteManyAsync_RemovesAll()
    {
        var prefix = $"deletemany-{Guid.NewGuid():N}";
        var categories = Enumerable.Range(0, 2)
            .Select(i => new Category { Name = $"{prefix}-{i}" })
            .ToArray();
        await _host.SeedAsync(categories);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();
        var loaded = await repository.GetListAsync(c => c.Name.StartsWith(prefix));
        await repository.DeleteManyAsync(loaded);

        Assert.Equal(0, await repository.GetCountAsync(c => c.Name.StartsWith(prefix)));
    }

    [Fact]
    public async Task UpdateAsync_AutoSaveFalse_PendsUntilUnitOfWorkCommit()
    {
        var category = new Category { Name = $"category-{Guid.NewGuid():N}" };
        await _host.SeedAsync(category);
        var newName = $"renamed-{Guid.NewGuid():N}";
        using (var scope = _host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Category, long>>();

            var loaded = await repository.GetAsync(category.Id);
            loaded.Name = newName;
            await repository.UpdateAsync(loaded, autoSave: false);

            // 变更仍挂起，未落库
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            Assert.True(dbContext.ChangeTracker.HasChanges());

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = _host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();
            var updated = await repository.GetAsync(category.Id);
            Assert.Equal(newName, updated.Name);
        }
    }

    [Fact]
    public async Task UpdateAsync_AggregateRoot_RefreshesConcurrencyStamp()
    {
        var order = new Order { Title = $"order-{Guid.NewGuid():N}" };
        await _host.SeedAsync(order);
        var stampBefore = order.ConcurrencyStamp;

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
        var loaded = await repository.GetAsync(order.Id);
        loaded.Title = $"order-{Guid.NewGuid():N}";
        await repository.UpdateAsync(loaded);

        Assert.NotEqual(stampBefore, loaded.ConcurrencyStamp);
    }

    [Fact]
    public async Task InsertAsync_AggregateRoot_EnqueuesDomainEvents()
    {
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
        var order = new Order { Title = $"order-{Guid.NewGuid():N}" };
        order.AddDomainEvent(new OrderCreatedEvent());

        await repository.InsertAsync(order);

        var queueEventBus = scope.ServiceProvider.GetRequiredService<IQueueEventBus>();
        Assert.True(await queueEventBus.AnyQueueAsync());
    }

    [Fact]
    public async Task DomainEvents_EnqueuedOnce_CommitConverges()
    {
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
        var order = new Order { Title = $"order-{Guid.NewGuid():N}" };
        order.AddDomainEvent(new OrderCreatedEvent());
        await repository.InsertAsync(order);

        // 提交：发布事件 → SaveChanges —— 事件已清空则不再重复入队，循环收敛
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();

        var queueEventBus = scope.ServiceProvider.GetRequiredService<IQueueEventBus>();
        Assert.False(await queueEventBus.AnyQueueAsync());
    }

    private record OrderCreatedEvent : DomainEvent;
}
