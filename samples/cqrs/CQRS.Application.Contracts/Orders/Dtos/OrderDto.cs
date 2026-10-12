using Cike.Contracts.EntityDtos;
using CQRS.Domain.Shared.Enums;

namespace CQRS.Application.Contracts.Orders.Dtos;

// 输出 Dto 默认继承 AuditedEntityDto（Id + 审计字段）；输入 Dto（Create/Update）平铺
public class OrderDto : AuditedEntityDto<long>
{
    public string OrderNo { get; set; } = default!;

    public long BuyerId { get; set; }

    public OrderStatus Status { get; set; }

    public string StatusName => Status.ToString();

    public decimal TotalAmount { get; set; }

    public AddressDto Address { get; set; } = default!;

    public List<OrderLineDto> Lines { get; set; } = new();
}
