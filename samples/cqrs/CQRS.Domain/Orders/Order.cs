using CQRS.Domain.Orders.Events;
using CQRS.Domain.Shared.Enums;
using CQRS.Domain.ValueObjects;
using Cike.Data.Domain.Entities;

namespace CQRS.Domain.Orders;

public class Order : FullAuditedAggregateRoot<long>
{
    public string OrderNo { get; protected set; } = default!;

    public long BuyerId { get; protected set; }

    public OrderStatus Status { get; protected set; } = OrderStatus.Pending;

    public Address Address { get; protected set; } = default!;

    public decimal TotalAmount { get; protected set; }

    public List<OrderLine> Lines { get; protected set; } = new();

    private Order()
    {
    }

    public static Order Create(long buyerId, Address address, IEnumerable<OrderLine> lines)
    {
        var lineList = lines?.ToList() ?? [];
        if (lineList.Count == 0) throw new UserFriendlyException("Order must have at least one line.");
        if (buyerId <= 0) throw new UserFriendlyException("BuyerId must be greater than 0.");
        ArgumentNullException.ThrowIfNull(address);

        var order = new Order
        {
            OrderNo = $"ORD{DateTime.Now:yyyyMMddHHmmssfff}",
            BuyerId = buyerId,
            Status = OrderStatus.Pending,
            Address = address,
        };
        order.Lines.AddRange(lineList);
        order.TotalAmount = lineList.Aggregate(new Money(0), (sum, line) => sum + line.Subtotal).Amount;

        order.AddDomainEvent(new OrderPlacedEvent(order.OrderNo, buyerId, order.TotalAmount));
        return order;
    }

    public void Pay()
    {
        AssertStatus(OrderStatus.Pending, "pay");
        Status = OrderStatus.Paid;

        AddDomainEvent(new OrderPaidEvent(Id, OrderNo, BuyerId, TotalAmount));
    }

    public void Ship()
    {
        AssertStatus(OrderStatus.Paid, "ship");
        Status = OrderStatus.Shipped;

        AddDomainEvent(new OrderShippedEvent(Id, OrderNo, BuyerId, Address.ToString()));
    }

    public void Cancel()
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.Paid))
        {
            throw new UserFriendlyException($"Cannot cancel an order in status '{Status}'.");
        }
        Status = OrderStatus.Cancelled;

        AddDomainEvent(new OrderCancelledEvent(Id, OrderNo, BuyerId, TotalAmount));
    }

    private void AssertStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new UserFriendlyException($"Cannot {action} an order in status '{Status}' (expected '{expected}').");
        }
    }
}
