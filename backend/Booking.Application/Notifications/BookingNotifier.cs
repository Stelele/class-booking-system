using Booking.Application.Abstractions;
using Booking.Application.Notifications;
using Booking.Domain.Reminders;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Booking.Application.Notifications;

/// Sends booking confirmations over WhatsApp. NEVER throws — failures are
/// logged to ReminderLog so a broken number can never break a booking.
public sealed class BookingNotifier(
    IAppDbContext db,
    ITwilioSender twilio,
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
            if (string.IsNullOrEmpty(row.u.PhoneE164)) return;

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

            string sid;
            try
            {
                sid = await twilio.SendAsync(row.u.PhoneE164, body, ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "WhatsApp {Template} to {To} failed.", template, row.u.PhoneE164);
                db.ReminderLogs.Add(new ReminderLog
                {
                    To = row.u.PhoneE164, Date = row.s.Date,
                    Template = template, Result = "failed",
                });
                await db.SaveChangesAsync(ct);
                return;
            }
            db.ReminderLogs.Add(new ReminderLog
            {
                To = row.u.PhoneE164, Date = row.s.Date,
                Template = template, Result = "sent", TwilioSid = sid,
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
