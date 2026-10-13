using Cike.AspNetCore.MinimalAPIs;
using Cike.AspNetCore.MinimalAPIs.EndpointFilters;
using CQRS.Application.Orders.Commands;
using CQRS.Application.Contracts.Orders.Dtos;
using CQRS.Application.Orders.Queries;
using Cike.EventBus.Local;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CQRS.Service.Open.Services;

[AutoValidation]
public class OrderService : MinimalApiServiceBase
{
    public OrderService()
    {
        RouteOptions.EnabledAuthorization = false;
    }

    public async Task<Results<Ok<long>, BadRequest<string>>> CreateAsync(
        [FromServices] ILocalEventBus eventBus, CreateOrderDto dto, CancellationToken cancellationToken = default)
    {
        var command = new CreateOrderCommand(dto);
        await eventBus.PublishAsync(command, cancellationToken);
        return TypedResults.Ok(command.Id);
    }

    public async Task<Results<Ok<OrderDto>, NotFound>> GetAsync(
        [FromServices] ILocalEventBus eventBus, long id, CancellationToken cancellationToken = default)
    {
        var query = new GetOrderQuery(id);
        await eventBus.PublishAsync(query, cancellationToken);
        return query.Result == null ? TypedResults.NotFound() : TypedResults.Ok(query.Result);
    }

    public async Task<Ok<PagedResultDto<OrderDto>>> GetPagedListAsync(
        [FromServices] ILocalEventBus eventBus,
        string? keyword, long? buyerId,
        [AsParameters] PagedAndSortedResultRequest paging,   // 分页参数从查询串直接绑定进对象
        CancellationToken cancellationToken = default)
    {
        var query = new GetOrderPagedListQuery(keyword, buyerId, paging);
        await eventBus.PublishAsync(query, cancellationToken);
        return TypedResults.Ok(query.Result ?? new PagedResultDto<OrderDto>());
    }

    public async Task<Ok> PayAsync(
        [FromServices] ILocalEventBus eventBus, long id, CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(new PayOrderCommand(id), cancellationToken);
        return TypedResults.Ok();
    }

    public async Task<Ok> ShipAsync(
        [FromServices] ILocalEventBus eventBus, long id, CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(new ShipOrderCommand(id), cancellationToken);
        return TypedResults.Ok();
    }

    public async Task<Ok> CancelAsync(
        [FromServices] ILocalEventBus eventBus, long id, CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(new CancelOrderCommand(id), cancellationToken);
        return TypedResults.Ok();
    }

    public async Task<Ok> DeleteAsync(
        [FromServices] ILocalEventBus eventBus, long id, CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(new DeleteOrderCommand(id), cancellationToken);
        return TypedResults.Ok();
    }
}
