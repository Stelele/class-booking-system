using Booking.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.WhatsApp;

/// Log-only fallback when Twilio:AccountSid is empty (dev/E2E/CI).
public sealed class NullTwilioSender(ILogger<NullTwilioSender> log) : ITwilioSender
{
    public Task<string> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        log.LogInformation("Twilio unconfigured — would send to {To}: {Body}", toE164, body);
        return Task.FromResult("SM-LOG-ONLY");
    }
}
