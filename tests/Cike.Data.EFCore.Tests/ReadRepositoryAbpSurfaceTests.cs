using Cike.Data.Domain.Repositories;

namespace Cike.Data.EFCore.Tests;

/// <summary>
/// 精简后查询面的回归：WithDetailsAsync(propertyPaths) 导航预加载、
/// 单数写方法内部复用批量路径（Insert→InsertMany 等）后的行为等价性。
/// </summary>
public class ReadRepositoryAbpSurfaceTests : IClassFixture<CikeEfCoreTestHost>
{
    private readonly CikeEfCoreTestHost _host;

    public ReadRepositoryAbpSurfaceTests(CikeEfCoreTestHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task WithDetailsAsync_PropertyPaths_IncludesSpecifiedNavigation()
    {
        var marker = Guid.NewGuid().ToString("N");
        var order = new Order { Title = $"order-{marker}" };
        order.Lines.Add(new OrderLine { ProductName = $"line-{marker}", Quantity = 1 });
        order.Lines.Add(new OrderLine { ProductName = $"line-{marker}", Quantity = 2 });
        await _host.SeedAsync(order);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Order, long>>();

        var queryable = repository.WithDetails(o => o.Lines);

        var loaded = await queryable.FirstOrDefaultAsync(o => o.Title == $"order-{marker}");
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Lines.Count);
    }

    [Fact]
    public async Task GetPagedListAsync_RequestForm_SortingAndPagingStillWork()
    {
        var prefix = await SeedCategoriesAsync(5);
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var page1 = await repository.GetPagedListAsync(new PagedRequest(1, 2, "Name"), c => c.Name.StartsWith(prefix));
        var page3 = await repository.GetPagedListAsync(new PagedRequest(3, 2, "Name"), c => c.Name.StartsWith(prefix));

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal($"{prefix}-00", page1.Items[0].Name);
        Assert.Equal($"{prefix}-01", page1.Items[1].Name);
        Assert.Equal(5, page3.Total);
        Assert.Single(page3.Items);
    }

    /// <summary>插入 count 个唯一前缀、带序号的 Category 并提交，返回前缀。</summary>
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
