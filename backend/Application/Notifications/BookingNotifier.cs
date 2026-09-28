using Application.Abstractions;
using Application.Notifications;
using Domain.Reminders;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Notifications;

/// <summary>Sends booking confirmations. NEVER throws — failures are
/// logged to ReminderLog so a broken notification can never break a booking.</summary>
public sealed class BookingNotifier(
    IAppDbContext db,
    INotifier notifier,
    ILogger<BookingNotifier> log) : IBookingNotifier
{
    public async Task NotifyBookingChangedAsync(Guid bookingId, BookingChangeKind kind, CancellationToken ct)
    {
        try
        {
            var row = await db.Bookings.AsNoTracking()
                .Where(b => b.Id == bookingId)
                .Join(db.Slots, b => b.SlotId, s => s.Id, (b, s) => new { b, s })
                .Join(db.Users, x => x.b.StudentId, u => u.Id, (x, u) => new { x.b, x.s, u })
                .FirstOrDefaultAsync(ct);
            if (row is null) return;

            var london = ReminderSchedule.StudentLocalTime(row.s.Date);
            var link = row.s.MeetLink ?? "";
            var (template, body) = kind switch
            {
                BookingChangeKind.Cancelled => ("cancelled",
                    ReminderMessages.Cancelled(row.u.Name, row.s.Date, london)),
                BookingChangeKind.Rescheduled => ("rescheduled",
                    ReminderMessages.Rescheduled(row.u.Name, row.s.Date, london,
                        row.b.OriginalDate ?? row.s.Date, link)),
                _ => ("confirmation",
                    ReminderMessages.Confirmation(row.u.Name, row.s.Date, london, link)),
            };

            NotifyResult result;
            try
            {
                result = await notifier.SendAsync(
                    row.u.Id, ReminderTitles.For(template), body, NotifyUrgency.High, ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Notification {Template} to {UserId} failed.", template, row.u.Id);
                db.ReminderLogs.Add(new ReminderLog
                {
                    UserId = row.u.Id, To = row.u.Email, Date = row.s.Date,
                    Template = template, Result = "failed",
                });
                await db.SaveChangesAsync(ct);
                return;
            }
            db.ReminderLogs.Add(new ReminderLog
            {
                UserId = row.u.Id, To = row.u.Email, Date = row.s.Date,
                Template = template, Result = "sent",
                Channel = result.Channel, ProviderRef = result.ProviderRef,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // belt and braces: notifications must never break bookings
            log.LogError(ex, "BookingNotifier failed for booking {BookingId}.", bookingId);
        }
    }
}
