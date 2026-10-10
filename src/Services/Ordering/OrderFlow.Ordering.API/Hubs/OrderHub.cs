using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OrderFlow.Ordering.API.Application;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Ordering.API.Hubs;

/// <summary>Real-time order status channel. Customers join their own group; admins also see every order.</summary>
[Authorize]
public sealed class OrderHub : Hub
{
    public const string Path = "/hubs/orders";
    public const string AdminsGroup = "admins";
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(user.GetUserId()));
        if (user.IsAdmin()) await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        await base.OnConnectedAsync();
    }
}

public sealed class OrderNotifier(IHubContext<OrderHub> hub, ILogger<OrderNotifier> logger)
{
    public async Task OrderUpdatedAsync(OrderDto order, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.Groups(OrderHub.UserGroup(order.UserId), OrderHub.AdminsGroup)
                .SendAsync("orderUpdated", order, ct);
        }
        catch (Exception ex)
        {
            // Push is best-effort: the database is the source of truth and clients re-sync on reconnect.
            logger.LogWarning(ex, "Failed to push update for order {OrderNumber}", order.OrderNumber);
        }
    }
}
