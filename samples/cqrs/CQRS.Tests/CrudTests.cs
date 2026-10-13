using CQRS.Tests.Infrastructure;
using Xunit;

namespace CQRS.Tests;

public class CrudTests : IClassFixture<CQRSTestHost>
{
    private readonly CQRSTestHost _host;

    public CrudTests(CQRSTestHost host)
    {
        _host = host;
    }

    private static CreateOrderDto NewOrderDto(decimal amount = 100m, int lines = 1)
    {
        return new CreateOrderDto
        {
            BuyerId = Random.Shared.Next(1000, int.MaxValue),
            Address = new AddressDto
            {
                Province = "Guangdong",
                City = "Shenzhen",
                Street = "Tech Park",
                ZipCode = "518000",
            },
            Lines = Enumerable.Range(0, lines).Select(i => new OrderLineDto
            {
                ProductName = $"Product-{i}",
                Quantity = 2,
                UnitPrice = amount,
            }).ToList(),
        };
    }

    [Fact]
    public async Task Create_Then_Get_ReturnsDetailWithLinesAndAddress()
    {
        var command = new CreateOrderCommand(NewOrderDto(25.5m, lines: 2));
        await _host.PublishAsync(command);
        Assert.NotEqual(0, command.Id);

        var query = new GetOrderQuery(command.Id);
        await _host.PublishAsync(query);

        var dto = query.Result!;
        Assert.Equal(command.Id, dto.Id);
        Assert.StartsWith("ORD", dto.OrderNo);
        Assert.Equal(CQRS.Domain.Shared.Enums.OrderStatus.Pending, dto.Status);
        Assert.Equal(2, dto.Lines.Count);
        Assert.All(dto.Lines, l => Assert.Equal(2, l.Quantity));
        Assert.Equal("Shenzhen", dto.Address.City);
        Assert.Equal(102m, dto.TotalAmount); // 2 行 × 2 件 × 25.5
    }

    [Fact]
    public async Task GetList_FiltersByBuyer_AndPages()
    {
        var buyerId = Random.Shared.Next(1000, int.MaxValue);
        for (var i = 0; i < 3; i++)
        {
            var dto = NewOrderDto();
            dto.BuyerId = buyerId;
            await _host.PublishAsync(new CreateOrderCommand(dto));
        }

        var query = new GetOrderPagedListQuery(Keyword: null, BuyerId: buyerId,
            Paging: new PagedAndSortedResultRequest { Page = 1, PageSize = 2 });
        await _host.PublishAsync(query);

        var page = query.Result!;
        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, o => Assert.Equal(buyerId, o.BuyerId));
    }

    [Fact]
    public async Task Pay_Then_Ship_ChangesStatus()
    {
        var command = new CreateOrderCommand(NewOrderDto());
        await _host.PublishAsync(command);

        await _host.PublishAsync(new PayOrderCommand(command.Id));
        await _host.PublishAsync(new ShipOrderCommand(command.Id));

        var query = new GetOrderQuery(command.Id);
        await _host.PublishAsync(query);
        Assert.Equal(CQRS.Domain.Shared.Enums.OrderStatus.Shipped, query.Result!.Status);
    }

    [Fact]
    public async Task Delete_IsSoftDelete_And_Idempotent()
    {
        var command = new CreateOrderCommand(NewOrderDto());
        await _host.PublishAsync(command);

        await _host.PublishAsync(new DeleteOrderCommand(command.Id));
        await _host.PublishAsync(new DeleteOrderCommand(command.Id));

        var query = new GetOrderQuery(command.Id);
        await Assert.ThrowsAsync<UserFriendlyException>(() => _host.PublishAsync(query));

        using var scope = _host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CQRS.EntityFrameworkCore.CqrsDbContext>();
        Assert.True(await dbContext.Orders.IgnoreQueryFilters().AnyAsync(o => o.Id == command.Id));
    }

    [Fact]
    public async Task Get_MissingOrder_ThrowsUserFriendly()
    {
        var query = new GetOrderQuery(-1);
        await Assert.ThrowsAsync<UserFriendlyException>(() => _host.PublishAsync(query));
    }
}
