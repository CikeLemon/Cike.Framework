using Cike.Cqrs.Commands;

namespace CQRS.Application.Orders.Commands;

public record PayOrderCommand(long OrderId) : Command;
