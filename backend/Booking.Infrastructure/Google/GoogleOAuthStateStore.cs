using System.Collections.Concurrent;
using System.Security.Cryptography;
using Booking.Application.Abstractions;

namespace Booking.Infrastructure.Google;

public sealed class GoogleOAuthStateStore : IGoogleOAuthStateStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _states = new();

    public string Issue()
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        _states[state] = DateTimeOffset.UtcNow.AddMinutes(10);
        return state;
    }

    public bool Consume(string state)
    {
        if (!_states.TryRemove(state, out var expiry)) return false;
        return DateTimeOffset.UtcNow <= expiry;
    }
}
