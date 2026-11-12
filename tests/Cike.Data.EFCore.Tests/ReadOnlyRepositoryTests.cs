using Cike.Data.Domain.Repositories;

namespace Cike.Data.EFCore.Tests;

/// <summary>
/// 工单 2 验收：只读仓储首条贯通线——AddCikeDbContext 后默认仓储可注入，
/// 按 Id 查询语义正确（GetAsync 抛异常 / FindAsync 返回 null）。
/// </summary>
public class ReadOnlyRepositoryTests : IClassFixture<CikeEfCoreTestHost>
{
    private readonly CikeEfCoreTestHost _host;

    public ReadOnlyRepositoryTests(CikeEfCoreTestHost host)
    {
        _host = host;
    }

    [Fact]
    public void Resolve_DefaultRepository_IsEfCoreRepository()
    {
        using var scope = _host.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        Assert.IsType<EfCoreRepository<TestDbContext, Category, long>>(repository);
    }

    [Fact]
    public async Task GetAsync_Exists_ReturnsEntity()
    {
        var id = await SeedCategoryAsync();
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var found = await repository.GetAsync(id);

        Assert.Equal(id, found.Id);
    }

    [Fact]
    public async Task FindAsync_NotExists_ReturnsNull()
    {
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var found = await repository.FindAsync(-1);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetAsync_NotExists_ThrowsUserFriendlyException()
    {
        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Category, long>>();

        var exception = await Assert.ThrowsAsync<UserFriendlyException>(() => repository.GetAsync(-1));
        Assert.Contains("-1", exception.Message);
    }

    /// <summary>插入一个唯一命名的 Category 并提交，返回生成的主键。</summary>
    private async Task<long> SeedCategoryAsync()
    {
        var category = new Category { Name = $"category-{Guid.NewGuid():N}" };
        await _host.SeedAsync(category);
        return category.Id;
    }
}
