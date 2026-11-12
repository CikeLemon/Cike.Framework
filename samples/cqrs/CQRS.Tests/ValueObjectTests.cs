using Cike.Data.Domain.Repositories;
using CQRS.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CQRS.Tests;

public class ValueObjectTests : IClassFixture<CQRSTestHost>
{
    private readonly CQRSTestHost _host;

    public ValueObjectTests(CQRSTestHost host)
    {
        _host = host;
    }

    [Fact]
    public void ValueEquals_AllComponents()
    {
        Assert.Equal(new Address("Guangdong", "Shenzhen", "Tech Park", "518000"),
                     new Address("Guangdong", "Shenzhen", "Tech Park", "518000"));

        Assert.NotEqual(new Address("Guangdong", "Shenzhen", "Tech Park", "518000"),
                        new Address("Guangdong", "Guangzhou", "Tech Park", "518000"));

        Assert.True(new Money(10) == new Money(10));
        Assert.True(new Money(10) != new Money(11));
        Assert.True(new Money(10) != new Money(10, "USD"));
    }

    [Fact]
    public void Money_Arithmetic_RequiresSameCurrency()
    {
        var sum = new Money(1.5m) + new Money(2.25m);
        Assert.Equal(new Money(3.75m), sum);

        Assert.Throws<UserFriendlyException>(() => (new Money(1, "CNY") + new Money(1, "USD")).ToString());
        Assert.Throws<UserFriendlyException>(() => new Money(-1m));
    }

    [Fact]
    public async Task Address_Persists_And_RoundTrips()
    {
        var command = new CreateOrderCommand(new CreateOrderDto
        {
            BuyerId = Random.Shared.Next(1000, int.MaxValue),
            Address = new AddressDto { Province = "Guangdong", City = "Shenzhen", Street = "Tech Park", ZipCode = "518000" },
            Lines = [new OrderLineDto { ProductName = "Product", Quantity = 1, UnitPrice = 10 }],
        });
        await _host.PublishAsync(command);

        using var scope = _host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<Order, long>>();
        var order = await repository.WithDetails(o => o.Lines).FirstAsync(o => o.Id == command.Id);

        Assert.Equal(new Address("Guangdong", "Shenzhen", "Tech Park", "518000"), order.Address);
        Assert.Equal(new Money(10), order.Lines[0].UnitPrice);
        Assert.Equal(new Money(10), order.Lines[0].Subtotal);
    }
}
