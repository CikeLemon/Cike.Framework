using Cike.Data.Domain.Entities;

namespace CQRS.Domain.Buyers;

public class Buyer : FullAuditedAggregateRoot<long>
{
    public string Name { get; protected set; } = default!;

    public int OrderCount { get; protected set; }

    public decimal TotalAmount { get; protected set; }

    private Buyer()
    {
    }

    private Buyer(long id, string name)
    {
        Id = id;
        Name = name;
    }

    public static Buyer Create(long id, string name)
    {
        if (id <= 0) throw new UserFriendlyException("BuyerId must be greater than 0.");
        if (name.IsNullOrWhiteSpace()) throw new UserFriendlyException("Buyer name cannot be empty.");

        return new Buyer(id, name);
    }

    public void RecordPlaced(decimal amount)
    {
        OrderCount++;
        TotalAmount += amount;
    }

    public void RecordCancelled(decimal amount)
    {
        OrderCount--;
        TotalAmount -= amount;
    }
}
