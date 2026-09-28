using Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Notifications;

/// <summary>
/// Push first, email only when push did not reach a device. Handles
/// push-only, email-only and unconfigured (log-only) deployments by checking
/// each collaborator's Enabled flag. Never throws: a reminder that cannot be
/// delivered is logged, not raised, so it can never break a booking.
/// </summary>
public sealed class PushOrEmailNotifier(
    IAppDbContext db,
    WebPushSender push,
    EmailNotifier email,
    ILogger<PushOrEmailNotifier> log) : INotifier
{
    public async Task<NotifyResult> SendAsync(
        Guid userId,
        string title,
        string body,
        NotifyUrgency urgency = NotifyUrgency.Normal,
        CancellationToken ct = default)
    {
        var needsEmail = !email.Enabled;

        if (push.Enabled)
        {
            var outcome = await push.SendAsync(userId, title, body, urgency, ct);
            if (outcome.Delivered > 0)
            {
                if (!needsEmail && outcome.IosDevices > 0)
                {
                    // Web Push only ever confirms the push service accepted the
                    // message — never that the device displayed it. On iOS we
                    // cannot verify display, and a silently dropped lesson
                    // reminder is the one failure this whole fallback exists to
                    // prevent. So iOS devices get both channels. Everywhere
                    // else push is trusted and email stays a fallback.
                    log.LogInformation(
                        "Also emailing user {UserId}: {Ios} iOS subscription(s) cannot be confirmed as displayed.",
                        userId, outcome.IosDevices);
                    needsEmail = true;
                }
                else if (!needsEmail)
                {
                    return new NotifyResult("push");
                }
            }
            else
            {
                log.LogInformation(
                    "No push delivered for user {UserId} ({Failed} failed, {Pruned} pruned).",
                    userId, outcome.Failed, outcome.Pruned);
            }
        }

        if (!email.Enabled) return new NotifyResult("log");

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return new NotifyResult("log");

        return await email.SendToAsync(user.Email, user.Name, title, body, ct);
    }
}
