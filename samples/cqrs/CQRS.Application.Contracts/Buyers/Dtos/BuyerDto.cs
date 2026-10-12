using Cike.Contracts.EntityDtos;

namespace CQRS.Application.Contracts.Buyers.Dtos;

public class BuyerDto : EntityDto<long>
{
    public string Name { get; set; } = default!;

    public int OrderCount { get; set; }

    public decimal TotalAmount { get; set; }
}
