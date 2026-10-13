using CQRS.Application.Contracts.Orders.Dtos;
using Cike.Cqrs.Queries;
using Cike.Contracts.EntityDtos;

namespace CQRS.Application.Orders.Queries;

// 分页参数打包为 PagedAndSortedResultRequest 对象（零组装直传仓储），业务筛选参数平铺；
// 筛选字段多时才改用继承 PagedAndSortedResultRequest 的 Dto
public record GetOrderPagedListQuery(string? Keyword, long? BuyerId, PagedAndSortedResultRequest Paging)
    : Query<PagedResultDto<OrderDto>>;
