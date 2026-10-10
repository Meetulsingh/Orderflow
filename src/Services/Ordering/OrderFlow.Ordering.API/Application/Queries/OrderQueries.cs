using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Ordering.API.Domain;
using OrderFlow.Ordering.API.Infrastructure;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Ordering.API.Application.Queries;

// Read side of CQRS: no-tracking queries shaped for the UI, never touching aggregate behaviour.

public sealed record GetMyOrdersQuery(Guid UserId, int Take = 50) : IRequest<IReadOnlyList<OrderDto>>;

public sealed record GetOrderQuery(Guid OrderId, Guid UserId, bool IsAdmin) : IRequest<OrderDto>;

public sealed record GetRecentOrdersQuery(int Take = 50) : IRequest<IReadOnlyList<OrderDto>>;

public sealed record GetOrderStatsQuery : IRequest<OrderStatsDto>;

public sealed class OrderQueryHandlers(OrderingDbContext db, TimeProvider clock) :
    IRequestHandler<GetMyOrdersQuery, IReadOnlyList<OrderDto>>,
    IRequestHandler<GetOrderQuery, OrderDto>,
    IRequestHandler<GetRecentOrdersQuery, IReadOnlyList<OrderDto>>,
    IRequestHandler<GetOrderStatsQuery, OrderStatsDto>
{
    public async Task<IReadOnlyList<OrderDto>> Handle(GetMyOrdersQuery query, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking().AsSplitQuery()
            .Where(o => o.UserId == query.UserId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(Math.Clamp(query.Take, 1, 100))
            .ToListAsync(ct);
        return orders.Select(OrderDto.From).ToList();
    }

    public async Task<OrderDto> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == query.OrderId, ct);
        if (order is null || (!query.IsAdmin && order.UserId != query.UserId))
            throw new NotFoundException("Order not found.");
        return OrderDto.From(order);
    }

    public async Task<IReadOnlyList<OrderDto>> Handle(GetRecentOrdersQuery query, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking().AsSplitQuery()
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(Math.Clamp(query.Take, 1, 200))
            .ToListAsync(ct);
        return orders.Select(OrderDto.From).ToList();
    }

    public async Task<OrderStatsDto> Handle(GetOrderStatsQuery query, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddHours(-24);
        var byStatus = await db.Orders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Sum = g.Sum(o => o.Total) })
            .ToListAsync(ct);
        var last24h = await db.Orders.CountAsync(o => o.CreatedAtUtc >= since, ct);

        int Count(OrderStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
        var completed = byStatus.FirstOrDefault(x => x.Status == OrderStatus.Completed);
        var revenue = completed?.Sum ?? 0m;
        var completedCount = completed?.Count ?? 0;

        return new OrderStatsDto(
            byStatus.Sum(x => x.Count),
            Count(OrderStatus.Pending),
            Count(OrderStatus.PaymentProcessing),
            completedCount,
            Count(OrderStatus.Failed),
            revenue,
            completedCount == 0 ? 0 : Math.Round(revenue / completedCount, 2),
            last24h);
    }
}
