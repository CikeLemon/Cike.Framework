using Cike.Data.Domain.Repositories;

namespace Cike.Data.EFCore.Tests;

/// <summary>
/// 工单 3 验收：条件列表、分页与计数——谓词过滤、分页元组（Total/Items）、Sorting 排序、
/// 计数与存在性判断、多租户实体按当前租户过滤。
/// </summary>
public class ListQueryTests : IClassFixture<CikeEfCoreTestHost>
{
    private readonly CikeEfCoreTestHost _host;

    public ListQueryTests(CikeEfCoreTestHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task GetListAsync_WithPredicate_ReturnsOnlyMatching()
    {
        var prefix = await SeedCategoriesAsync(2);
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var list = await repository.GetListAsync(c => c.Name.StartsWith(prefix));

        Assert.Equal(2, list.Count);
        Assert.All(list, c => Assert.StartsWith(prefix, c.Name));
    }

    [Fact]
    public async Task GetPagedListAsync_ReturnsTotalAndCurrentPage()
    {
        var prefix = await SeedCategoriesAsync(5);
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var page1 = await repository.GetPagedListAsync(new PagedRequest(1, 2), c => c.Name.StartsWith(prefix));
        var page3 = await repository.GetPagedListAsync(new PagedRequest(3, 2), c => c.Name.StartsWith(prefix));

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(5, page3.Total);
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task GetPagedListAsync_Sorting_AppliesOrderByString()
    {
        var prefix = await SeedCategoriesAsync(3);
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var ascending = await repository.GetPagedListAsync(new PagedRequest(1, 3, "Name"), c => c.Name.StartsWith(prefix));
        var descending = await repository.GetPagedListAsync(new PagedRequest(1, 3, "Name desc"), c => c.Name.StartsWith(prefix));

        Assert.Equal(ascending.Items.Select(c => c.Name).OrderBy(n => n), ascending.Items.Select(c => c.Name));
        Assert.Equal(ascending.Items.Select(c => c.Name).OrderByDescending(n => n), descending.Items.Select(c => c.Name));
    }

    [Fact]
    public async Task GetCountAsync_And_AnyAsync_WithPredicate()
    {
        var prefix = await SeedCategoriesAsync(2);
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var count = await repository.GetCountAsync(c => c.Name.StartsWith(prefix));
        var any = await repository.AnyAsync(c => c.Name.StartsWith(prefix));
        var none = await repository.AnyAsync(c => c.Name == $"{prefix}-not-exists");

        Assert.Equal(2, count);
        Assert.True(any);
        Assert.False(none);
    }

    [Fact]
    public async Task GetPagedListAsync_PageBeyondRange_EmptyItemsSameTotal()
    {
        var prefix = await SeedCategoriesAsync(2);
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var page = await repository.GetPagedListAsync(new PagedRequest(99, 2), c => c.Name.StartsWith(prefix));

        Assert.Equal(2, page.Total);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetPagedListAsync_PageSizeZero_ReturnsAll()
    {
        var prefix = await SeedCategoriesAsync(3);
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var page = await repository.GetPagedListAsync(new PagedRequest(1, 0), c => c.Name.StartsWith(prefix));

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task GetPagedListAsync_NoMatch_ZeroTotalEmptyItems()
    {
        // 值先在表达式树外求值，谓词内只留参数比较
        var notExistsName = $"none-{Guid.NewGuid():N}";
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var page = await repository.GetPagedListAsync(new PagedRequest(1, 2), c => c.Name == notExistsName);

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetListAsync_And_GetCountAsync_WithoutPredicate_CoverAll()
    {
        var prefix = await SeedCategoriesAsync(2);
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var list = await repository.GetListAsync();
        var count = await repository.GetCountAsync();

        // 类内测试共享库（只增不减），断言"包含本次种子"而非精确值
        Assert.Contains(list, c => c.Name.StartsWith(prefix));
        Assert.True(count >= 2);
    }

    [Fact]
    public async Task GetListAsync_MultiTenant_FiltersByCurrentTenant()
    {
        var marker = Guid.NewGuid().ToString("N");
        try
        {
            _host.SetTenant(1);
            await _host.SeedAsync(Enumerable.Range(0, 2).Select(i => new TenantOrder { Title = $"order-{marker}-1-{i}" }).ToArray());
            _host.SetTenant(2);
            await _host.SeedAsync(new TenantOrder { Title = $"order-{marker}-2-0" });

            _host.SetTenant(1);
            using (var scope = _host.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<TenantOrder, long>>();
                var tenantAList = await repository.GetListAsync(o => o.Title.Contains(marker));
                Assert.Equal(2, tenantAList.Count);
            }

            _host.SetTenant(2);
            using (var scope = _host.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<TenantOrder, long>>();
                var tenantBList = await repository.GetListAsync(o => o.Title.Contains(marker));
                Assert.Single(tenantBList);
            }
        }
        finally
        {
            _host.SetTenant(null);
        }
    }

    /// <summary>插入 count 个唯一前缀的 Category 并提交，返回前缀。</summary>
    private async Task<string> SeedCategoriesAsync(int count)
    {
        var prefix = $"cat-{Guid.NewGuid():N}";
        var categories = Enumerable.Range(0, count)
            .Select(i => new Category { Name = $"{prefix}-{i:D2}" })
            .ToArray();
        await _host.SeedAsync(categories);
        return prefix;
    }

    /// <summary>测试用分页请求（接口属性为可写，不能用 record 的 init）。</summary>
    private class PagedRequest : IPagedAndSortedRequest
    {
        public PagedRequest(int page, int pageSize, string sorting = "")
        {
            Page = page;
            PageSize = pageSize;
            Sorting = sorting;
        }

        public int Page { get; set; }
        public int PageSize { get; set; }
        public string Sorting { get; set; } = string.Empty;
    }
}
