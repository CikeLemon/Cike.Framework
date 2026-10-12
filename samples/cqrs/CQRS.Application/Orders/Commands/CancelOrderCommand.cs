using Cike.Cqrs.Commands;

namespace CQRS.Application.Orders.Commands;

public record CancelOrderCommand(long OrderId) : Command;
