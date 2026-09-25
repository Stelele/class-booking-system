using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;

namespace Infrastructure.Google;

public sealed class GoogleOAuthStateStore : IGoogleOAuthStateStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _states = new();

    public string Issue(string purpose, string binding = "")
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _states)
            if (kv.Value.ExpiresAt <= now) _states.TryRemove(kv.Key, out _);
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        _states[state] = new Entry(purpose, binding, now.Add(Lifetime));
        return state;
    }

    public bool Consume(string state, string purpose, string binding = "")
    {
        if (string.IsNullOrEmpty(state)) return false;
        if (!_states.TryRemove(state, out var entry)) return false;
        if (DateTimeOffset.UtcNow > entry.ExpiresAt) return false;
        if (!string.Equals(entry.Purpose, purpose, StringComparison.Ordinal)) return false;
        return FixedTimeEquals(entry.Binding, binding);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? "");
        var b = Encoding.UTF8.GetBytes(right ?? "");
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private sealed record Entry(string Purpose, string Binding, DateTimeOffset ExpiresAt);
}
