using Cike.MultiTenancy;

namespace Cike.Data.EFCore.Tests.Infrastructure;

/// <summary>
/// 仓储测试基座：以与生产一致的方式（模块加载 + AddCikeDbContext）组装容器。
/// 数据库为共享单连接的 SQLite in-memory——连接存活期间库不丢失，跨 Scope 可见已提交数据。
/// 每个测试类一个实例（IClassFixture），类内测试共享同一数据库，用唯一数据隔离。
/// </summary>
public sealed class CikeEfCoreTestHost : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _serviceProvider = default!;

    public IServiceProvider ServiceProvider => _serviceProvider;

    /// <summary>当前用户替身：测试内可直接改写 Id / TenantId（注意复位，避免类内测试间泄漏）。</summary>
    public FakeCurrentUser CurrentUser => _serviceProvider.GetRequiredService<FakeCurrentUser>();

    /// <summary>
    /// 切换当前租户。实体写入的 TenantId 走 ICurrentTenantAccessor（AsyncLocal），
    /// 查询过滤器走 ICurrentUser——两个源都设置；传 null 复位（租户 0）。
    /// </summary>
    public void SetTenant(long? tenantId)
    {
        CurrentUser.TenantId = tenantId;
        _serviceProvider.GetRequiredService<ICurrentTenantAccessor>().SetTenantId(tenantId ?? 0);
    }

    public async Task InitializeAsync()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();

        // 空配置占位：模块加载阶段 CikeDataModule 会读取 IConfiguration 解析连接串
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:Test"] = "DataSource=:memory:";
        services.ReplaceConfiguration(configuration);

        // 与生产一致：模块加载（依赖图 + 约定注册）走 AddApplicationAsync 扩展
        await services.AddApplicationAsync<CikeDataEFCoreTestsModule>();

        // 测试专用：把共享的 in-memory 连接注入 DbContextOptions（连接字符串本身不被使用）
        services.Configure<CikeDbContextOptions>(options =>
        {
            options.Configure(context => context.DbContextOptionsBuilder.UseSqlite(_connection));
        });
        services.AddCikeDbContext<TestDbContext>();

        _serviceProvider = services.BuildServiceProvider();

        // 建表
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        _connection.Dispose();
    }

    public IServiceScope CreateScope() => _serviceProvider.CreateScope();

    /// <summary>
    /// 插入实体并提交。多租户实体在跟踪时按当前用户写入 TenantId，需在调用前设置 CurrentUser。
    /// 类内测试共享数据库，种子数据用唯一值隔离。
    /// </summary>
    public async Task SeedAsync<TEntity>(params TEntity[] entities) where TEntity : class
    {
        using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
    }
}
