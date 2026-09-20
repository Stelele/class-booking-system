using Booking.Application.Abstractions;

namespace Booking.Infrastructure.Auth;

public static class E2eCodeStore
{
    public static string Last = "";
}

public sealed class E2eCodeSender : ICodeSender
{
    public Task SendAsync(string email, string name, string code, CancellationToken ct = default)
    {
        E2eCodeStore.Last = code;
        return Task.CompletedTask;
    }
}
