namespace Cike.Data.Domain.Entities;

public abstract class Entity<TKey> : IEntity<TKey>
{
    public TKey Id { get; set; } = default!;

    public object[] GetKeys()
    {
        return [Id!];
    }

    public virtual void SetId(TKey id)
    {
        Id = id;
    }
}
