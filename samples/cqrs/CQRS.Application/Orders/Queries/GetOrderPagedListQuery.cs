using CQRS.Application.Contracts.Orders.Dtos;
using Cike.Cqrs.Queries;
using Cike.Contracts.EntityDtos;

namespace CQRS.Application.Orders.Queries;

public record GetOrderListQuery(GetOrderListDto Input) : Query<PagedResultDto<OrderDto>>;
