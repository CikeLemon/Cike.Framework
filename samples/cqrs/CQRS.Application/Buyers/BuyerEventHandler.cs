using CQRS.Domain.Buyers;
using CQRS.Domain.Orders.Events;
using Cike.Domain.Repositories;
using Cike.EventBus.Local;

namespace CQRS.Application.Buyers;

public class BuyerEventHandler(IRepository<Buyer, long> buyerRepository)
{
    [LocalEventHandler]
    public async Task OnOrderPlacedAsync(OrderPlacedEvent @event, CancellationToken cancellationToken = default)
    {
        var buyer = await buyerRepository.FindAsync(@event.BuyerId, cancellationToken);
        if (buyer == null)
        {
            buyer = Buyer.Create(@event.BuyerId, $"Buyer-{@event.BuyerId}");
            buyer.RecordPlaced(@event.TotalAmount);
            await buyerRepository.InsertAsync(buyer, cancellationToken: cancellationToken);
        }
        else
        {
            buyer.RecordPlaced(@event.TotalAmount);
            await buyerRepository.UpdateAsync(buyer, cancellationToken: cancellationToken);
        }
    }

    [LocalEventHandler]
    public async Task OnOrderCancelledAsync(OrderCancelledEvent @event, CancellationToken cancellationToken = default)
    {
        var buyer = await buyerRepository.FindAsync(@event.BuyerId, cancellationToken)
            ?? throw new UserFriendlyException($"Buyer {@event.BuyerId} is not found, cannot rollback statistics.");

        buyer.RecordCancelled(@event.RefundAmount);
        await buyerRepository.UpdateAsync(buyer, cancellationToken: cancellationToken);
    }
}
