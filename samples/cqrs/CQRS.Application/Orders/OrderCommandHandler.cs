using CQRS.Application.Orders.Commands;
using CQRS.Application.Contracts.Orders.Dtos;
using CQRS.Domain.Orders;
using CQRS.Domain.ValueObjects;
using Cike.Domain.Repositories;
using Cike.EventBus.Local;

namespace CQRS.Application.Orders;

public class OrderCommandHandler(IRepository<Order, long> orderRepository)
{
    [LocalEventHandler]
    public async Task CreateAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var dto = command.Dto;
        var address = new Address(dto.Address.Province, dto.Address.City, dto.Address.Street, dto.Address.ZipCode);
        var lines = dto.Lines.Select(l => new OrderLine(l.ProductName, l.Quantity, new Money(l.UnitPrice)));

        var order = Order.Create(dto.BuyerId, address, lines);
        await orderRepository.InsertAsync(order, cancellationToken: cancellationToken);

        command.Id = order.Id;
    }

    [LocalEventHandler]
    public async Task PayAsync(PayOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetAsync(command.OrderId, cancellationToken);
        order.Pay();
        await orderRepository.UpdateAsync(order, cancellationToken: cancellationToken);
    }

    [LocalEventHandler]
    public async Task ShipAsync(ShipOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetAsync(command.OrderId, cancellationToken);
        order.Ship();
        await orderRepository.UpdateAsync(order, cancellationToken: cancellationToken);
    }

    [LocalEventHandler]
    public async Task CancelAsync(CancelOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetAsync(command.OrderId, cancellationToken);
        order.Cancel();
        await orderRepository.UpdateAsync(order, cancellationToken: cancellationToken);
    }

    [LocalEventHandler]
    public async Task DeleteAsync(DeleteOrderCommand command, CancellationToken cancellationToken = default)
    {
        await orderRepository.DeleteAsync(command.OrderId, cancellationToken: cancellationToken);
    }
}
