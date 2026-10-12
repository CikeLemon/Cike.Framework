using CQRS.Application.Contracts.Buyers.Dtos;
using CQRS.Application.Buyers.Queries;
using CQRS.Domain.Buyers;
using Cike.Domain.Repositories;
using Cike.EventBus.Local;
using Mapster;

namespace CQRS.Application.Buyers;

public class BuyerQueryHandler(IReadOnlyRepository<Buyer, long> buyerRepository)
{
    [LocalEventHandler]
    public async Task GetAsync(GetBuyerQuery query, CancellationToken cancellationToken = default)
    {
        using (buyerRepository.BeginAsNoTracking())
        {
            var buyer = await buyerRepository.FindAsync(query.Id, cancellationToken)
                ?? throw new UserFriendlyException($"Buyer {query.Id} is not found.");

            query.Result = buyer.Adapt<BuyerDto>();
        }
    }
}
