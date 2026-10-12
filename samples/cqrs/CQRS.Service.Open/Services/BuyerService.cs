using Cike.AspNetCore.MinimalAPIs;
using CQRS.Application.Contracts.Buyers.Dtos;
using CQRS.Application.Buyers.Queries;
using Cike.EventBus.Local;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CQRS.Service.Open.Services;

public class BuyerService : MinimalApiServiceBase
{
    public BuyerService()
    {
        RouteOptions.EnabledAuthorization = false;
    }

    public async Task<Results<Ok<BuyerDto>, NotFound>> GetAsync(
        [FromServices] ILocalEventBus eventBus, long id, CancellationToken cancellationToken = default)
    {
        var query = new GetBuyerQuery(id);
        await eventBus.PublishAsync(query, cancellationToken);
        return query.Result == null ? TypedResults.NotFound() : TypedResults.Ok(query.Result);
    }
}
