using Cike.Data.Domain.Entities;

namespace Cike.Data.Domain.AggregateRoots;

public abstract class AggregateRoot<TKey> : Entity<TKey>, IAggregateRoot<TKey>
{
    private readonly List<IDomainEvent> _events = new List<IDomainEvent>();

    public IEnumerable<IDomainEvent> DomainEvents => _events;

    public void AddDomainEvent(IDomainEvent domainEvent)
    {
        _events.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _events.Clear();
    }
}

public abstract class AggregateRoot : IAggregateRoot
{
    private readonly List<IDomainEvent> _events = new List<IDomainEvent>();

    public IEnumerable<IDomainEvent> DomainEvents => _events;

    public abstract object?[] GetKeys();

    public void AddDomainEvent(IDomainEvent domainEvent)
    {
        _events.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _events.Clear();
    }
}

