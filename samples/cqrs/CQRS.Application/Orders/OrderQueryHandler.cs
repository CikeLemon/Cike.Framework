using System.Linq.Expressions;
using CQRS.Application.Contracts.Orders.Dtos;
using CQRS.Application.Orders.Queries;
using CQRS.Domain.Orders;
using Cike.Contracts.EntityDtos;
using Cike.Contracts.Extensions;
using Cike.Domain.Repositories;
using Cike.EventBus.Local;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace CQRS.Application.Orders;

public class OrderQueryHandler(IReadOnlyRepository<Order, long> orderRepository)
{
    [LocalEventHandler]
    public async Task GetAsync(GetOrderQuery query, CancellationToken cancellationToken = default)
    {
        using (orderRepository.BeginAsNoTracking())
        {
            var order = await orderRepository
                .WithDetails(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == query.Id, cancellationToken)
                ?? throw new UserFriendlyException($"Order {query.Id} is not found.");

            query.Result = order.Adapt<OrderDto>();
        }
    }

    [LocalEventHandler]
    public async Task GetPagedListAsync(GetOrderPagedListQuery query, CancellationToken cancellationToken = default)
    {
        var keyword = query.Keyword;
        var buyerId = query.BuyerId;

        Expression<Func<Order, bool>>? predicate = null;
        if (keyword != null || buyerId.HasValue)
        {
            predicate = o => (keyword == null || o.OrderNo.Contains(keyword))
                          && (buyerId == null || o.BuyerId == buyerId.Value);
        }

        using (orderRepository.BeginAsNoTracking())
        {
            var queryable = orderRepository.WithDetails(o => o.Lines);
            if (predicate != null)
            {
                queryable = queryable.Where(predicate);
            }

            var (total, items) = await queryable.ToPaginationAsync(query.Paging, cancellationToken);
            query.Result = new PagedResultDto<OrderDto>(
                total, items.Select(o => o.Adapt<OrderDto>()).ToList());
        }
    }
}
