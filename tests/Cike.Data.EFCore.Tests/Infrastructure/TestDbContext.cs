namespace Cike.Data.EFCore.Tests.Infrastructure;

public class TestDbContext(DbContextOptions<TestDbContext> options, IServiceProvider serviceProvider)
    : CikeDbContext<TestDbContext>(options, serviceProvider)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<TenantOrder> TenantOrders => Set<TenantOrder>();
    public DbSet<GuidOrder> GuidOrders => Set<GuidOrder>();
}
