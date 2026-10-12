using CQRS.Application.Contracts.Orders.Dtos;
using Cike.Cqrs.Queries;

namespace CQRS.Application.Orders.Queries;

public record GetOrderQuery(long Id) : Query<OrderDto>;
