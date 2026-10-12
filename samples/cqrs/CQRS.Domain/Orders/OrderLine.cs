using CQRS.Domain.ValueObjects;
using Cike.Domain.Entities;

namespace CQRS.Domain.Orders;

public class OrderLine : Entity<long>
{
    public long OrderId { get; protected set; }

    public string ProductName { get; protected set; } = default!;

    public int Quantity { get; protected set; }

    public Money UnitPrice { get; protected set; } = default!;

    private OrderLine()
    {
    }

    public OrderLine(string productName, int quantity, Money unitPrice)
    {
        if (productName.IsNullOrWhiteSpace()) throw new UserFriendlyException("ProductName cannot be empty.");
        if (quantity <= 0) throw new UserFriendlyException("Quantity must be greater than 0.");

        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Money Subtotal => new(UnitPrice.Amount * Quantity, UnitPrice.Currency);
}
