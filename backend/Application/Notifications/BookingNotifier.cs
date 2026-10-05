using System.Globalization;
using Application.Abstractions;
using Application.Notifications;
using Domain.Reminders;
using Domain.Slots;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeZoneConverter;

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

            // Each recipient is attempted independently and neither failure may
            // suppress the other: a student-side delivery problem must not leave
            // the teacher unaware of a booking that was saved, which is the one
            // outcome this notification exists to prevent.
            try
            {
                var result = await notifier.SendAsync(
                    row.u.Id, ReminderTitles.For(template), body, NotifyUrgency.High, ct);
                db.ReminderLogs.Add(new ReminderLog
                {
                    UserId = row.u.Id, To = row.u.Email, Date = row.s.Date,
                    Template = template, Result = "sent",
                    Channel = result.Channel, ProviderRef = result.ProviderRef,
                });
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Notification {Template} to {UserId} failed.", template, row.u.Id);
                db.ReminderLogs.Add(new ReminderLog
                {
                    UserId = row.u.Id, To = row.u.Email, Date = row.s.Date,
                    Template = template, Result = "failed",
                });
            }

            if (kind == BookingChangeKind.Created)
            {
                // the student id, not "whoever booked this date": a teacher who
                // holds the day must not suppress another student's alert
                await NotifyTeacherAsync(row.s.Date, row.u.Id, row.u.Name, link, ct);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // belt and braces: notifications must never break bookings
            log.LogError(ex, "BookingNotifier failed for booking {BookingId}.", bookingId);
        }
    }

    /// Tells the teacher a student booked. Runs after the student's own message so
    /// the student is never left waiting on the teacher's send. The teacher is the
    /// one person who can actually teach the lesson, so a booking that they are
    /// not told about is a booking that does not happen.
    ///
    /// Uses the teacher's own Harare wall clock rather than
    /// <see cref="ReminderSchedule.StudentLocalTime"/>, which is fixed to the
    /// student's zone — reusing it would tell the teacher the wrong time.
    /// </summary>
    private async Task NotifyTeacherAsync(
        DateOnly date, Guid bookingStudentId, string studentName, string link, CancellationToken ct)
    {
        try
        {
            // Role is not EF-translatable as a string, so select ids in the query
            // and map in memory.
            var adminIds = await db.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Admin)
                .Select(u => u.Id)
                .ToListAsync(ct);
            if (adminIds.Count == 0) return;

            var teacher = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == adminIds[0], ct);
            if (teacher is null || string.IsNullOrWhiteSpace(teacher.Email)) return;
            // This booking is the teacher's own — they already got the student
            // message for it, so a second copy would be noise. This compares the
            // booking's own student, not "does the teacher hold this date": a
            // teacher who booked the day must not mute another student's alert.
            if (teacher.Id == bookingStudentId) return;

            var harare = TeacherLocalTime(date);
            var result = await notifier.SendAsync(
                teacher.Id,
                ReminderTitles.For("new_booking"),
                ReminderMessages.NewBooking(studentName, date, harare, link),
                NotifyUrgency.Normal, ct);

            // No SaveChanges here: the caller commits both log rows together, so
            // one write records the student outcome and the teacher outcome
            // together instead of two writes racing each other.
            db.ReminderLogs.Add(new ReminderLog
            {
                UserId = teacher.Id, To = teacher.Email, Date = date,
                Template = "new_booking", Result = "sent",
                Channel = result.Channel, ProviderRef = result.ProviderRef,
            });
        }
        catch (Exception ex)
        {
            // the booking already succeeded and the student is already notified;
            // a missing teacher alert must never undo either
            log.LogWarning(ex, "Teacher notification for {Date} failed.", date);
        }
    }

    /// "20:30" in the lesson's own zone — what the teacher reads off their clock.
    private static string TeacherLocalTime(DateOnly date)
    {
        var tz = TZConvert.GetTimeZoneInfo(LessonTime.ZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(LessonTime.StartUtc(date), tz);
        return local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
