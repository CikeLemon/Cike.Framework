namespace Cike.Contracts.EntityDtos;

public class EntityDto<TKey> : IEntityDto<TKey>
{
    public TKey Id { get; set; } = default!;
}

public interface IEntityDto<TKey>
{
    public TKey Id { get; set; }
}