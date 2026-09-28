using System.Collections.Concurrent;
using Lib.Net.Http.WebPush.Authentication;

namespace Infrastructure.Notifications;

/// <summary>
/// Caches signed VAPID tokens per push-service origin so the ES256 signature
/// is computed once per expiry window rather than once per message.
/// Registered as a singleton — the cache is the whole point.
/// </summary>
public sealed class MemoryVapidTokenCache : IVapidTokenCache
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, string Token)> _tokens = new();

    public string? Get(string origin) =>
        _tokens.TryGetValue(origin, out var e) && e.Expires > DateTimeOffset.UtcNow
            ? e.Token
            : null;

    public void Put(string origin, DateTimeOffset expiration, string token) =>
        _tokens[origin] = (expiration, token);
}
