using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace BeautyBookBackend.Services;

public sealed class AccountConnections
{
    private readonly ConcurrentDictionary<(Guid User, string Connection), HubCallerContext> connections = new();
    public void Register(Guid user, HubCallerContext context) => connections[(user, context.ConnectionId)] = context;
    public void Remove(Guid user, string connection) => connections.TryRemove((user, connection), out _);
    public Guid[] Users => connections.Keys.Select(x => x.User).Distinct().ToArray();
    public void Abort(Guid user)
    {
        foreach (var item in connections.Where(x => x.Key.User == user)) {
            if (connections.TryRemove(item.Key, out var context)) context.Abort();
        }
    }
}
