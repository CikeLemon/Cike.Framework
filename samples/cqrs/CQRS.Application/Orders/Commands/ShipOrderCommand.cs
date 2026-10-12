using Cike.Cqrs.Commands;

namespace CQRS.Application.Orders.Commands;

public record ShipOrderCommand(long OrderId) : Command;
