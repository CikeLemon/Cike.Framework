using CQRS.Application.Contracts.Orders.Dtos;
using Cike.Cqrs.Commands;

namespace CQRS.Application.Orders.Commands;

public record CreateOrderCommand(CreateOrderDto Dto) : Command
{
    public long Id { get; set; }
}
