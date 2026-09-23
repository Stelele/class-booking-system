using System.Collections.Concurrent;
using System.Security.Cryptography;
using Application.Abstractions;

namespace Infrastructure.Google;

public sealed class GoogleOAuthStateStore : IGoogleOAuthStateStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _states = new();

    public string Issue()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _states)
            if (kv.Value <= now) _states.TryRemove(kv.Key, out _);
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        _states[state] = now.AddMinutes(10);
        return state;
    }

    public bool Consume(string state)
    {
        if (!_states.TryRemove(state, out var expiry)) return false;
        return DateTimeOffset.UtcNow <= expiry;
    }
}
