using Application.Abstractions;
using Application.Notifications;
using Domain.Reminders;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TimeZoneConverter;

namespace Infrastructure.Notifications;

/// Timer worker (BackupWorker pattern): computes next fire across all rhythms,
/// sleeps until then, sends due reminders with per-recipient isolation.
public sealed class ReminderService(
    IServiceScopeFactory scopes,
    IConfiguration config,
    ILogger<ReminderService> log) : BackgroundService
{
    private static readonly TimeSpan CatchUpGrace = TimeSpan.FromMinutes(15);

    private bool Enabled(string rhythm) =>
        config.GetValue($"Reminder:{rhythm}", true);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } // let boot finish
        catch (OperationCanceledException) { return; }
        await CatchUpAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            var next = await ComputeNextFireAsync(ct);
            if (next is null)
            {
                try { await Task.Delay(TimeSpan.FromHours(1), ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            var delay = next.Value - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }
            }
            await FireDueAsync(DateTime.UtcNow, ct);
        }
    }

    private async Task CatchUpAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var now = DateTime.UtcNow;
        var fires = await CollectDueFiresAsync(db, now - CatchUpGrace, now, ct);
        foreach (var f in fires) await SendFireAsync(f, ct);
    }

    private async Task<DateTime?> ComputeNextFireAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var now = DateTime.UtcNow;
        DateTime? next = null;

        if (Enabled("Monday"))
        {
            var monday = ReminderSchedule.NextMondaySummaryUtc(now);
            next = Min(next, monday);
        }

        if (Enabled("Morning") || Enabled("Evening"))
        {
            // next lesson date at or after today with an active booking
            var today = DateOnly.FromDateTime(now);
            var dates = await db.Slots.AsNoTracking()
                .Where(s => s.Date >= today && s.Bookings.Any(b => b.Status == BookingStatus.Active))
                .OrderBy(s => s.Date).Select(s => s.Date).Take(14).ToListAsync(ct);
            foreach (var d in dates)
            {
                foreach (var f in ReminderSchedule.ForLesson(d))
                {
                    if ((f.Template == "morning" && !Enabled("Morning")) ||
                        (f.Template == "evening" && !Enabled("Evening")))
                        continue;
                    if (f.Utc > now) next = Min(next, f.Utc);
                }
            }
        }
        return next;
    }

    private async Task FireDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var fires = await CollectDueFiresAsync(db, nowUtc - TimeSpan.FromMinutes(5), nowUtc, ct);
        foreach (var f in fires) await SendFireAsync(f, ct);
    }

    internal sealed record DueFire(
        Guid StudentId, string Name, string Email, DateOnly LessonDate,
        string Template, string MeetLink, List<(DateOnly date, string localTime)>? Week = null);

    internal async Task<List<DueFire>> CollectDueFiresAsync(
        IAppDbContext db, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var fires = new List<DueFire>();

        // Every user is a candidate now — push needs no phone number.
        var students = await db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.Name, u.Email })
            .ToListAsync(ct);
        if (students.Count == 0) return fires;

        var studentIds = students.Select(s => s.Id).ToList();
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Active && studentIds.Contains(b.StudentId))
            .Join(db.Slots, b => b.SlotId, s => s.Id, (b, s) => new { b.StudentId, s.Date, s.MeetLink })
            .ToListAsync(ct);
        var byStudent = bookings.GroupBy(b => b.StudentId).ToDictionary(g => g.Key, g => g.ToList());

        // Idempotency key is (UserId, Date, Template). Rows written before push
        // notifications existed were keyed by phone; the migration backfilled
        // their UserId where it could, and any that could not are ignored here
        // (a user with no phone never received those reminders anyway).
        var sent = await db.ReminderLogs.AsNoTracking()
            .Where(r => r.Result == "sent" && r.UserId != null)
            .Select(r => new { UserId = r.UserId!.Value, r.Date, r.Template })
            .ToListAsync(ct);
        var sentKeys = sent.Select(r => (r.UserId, r.Date, r.Template)).ToHashSet();

        foreach (var s in students)
        {
            if (!byStudent.TryGetValue(s.Id, out var mine)) continue;

            // lesson nudges whose fire instants fall in the window
            foreach (var b in mine)
            {
                foreach (var f in ReminderSchedule.ForLesson(b.Date))
                {
                    if (f.Utc < fromUtc || f.Utc > toUtc) continue;
                    if ((f.Template == "morning" && !Enabled("Morning")) ||
                        (f.Template == "evening" && !Enabled("Evening")))
                        continue;
                    if (sentKeys.Contains((s.Id, b.Date, f.Template))) continue;
                    fires.Add(new DueFire(s.Id, s.Name, s.Email, b.Date, f.Template, b.MeetLink ?? ""));
                }
            }

            // monday summary due in the window
            if (Enabled("Monday"))
            {
                var mondayUtc = ReminderSchedule.NextMondaySummaryUtc(toUtc.AddDays(-7));
                if (mondayUtc >= fromUtc && mondayUtc <= toUtc)
                {
                    var weekStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                        mondayUtc, TimeZoneConverter.TZConvert.GetTimeZoneInfo(ReminderSchedule.ZoneId)));
                    var week = mine
                        .Where(b => b.Date >= weekStart && b.Date < weekStart.AddDays(7))
                        .OrderBy(b => b.Date)
                        .Select(b => (b.Date, ReminderSchedule.StudentLocalTime(b.Date)))
                        .ToList();
                    if (week.Count > 0 && !sentKeys.Contains((s.Id, weekStart, "monday")))
                        fires.Add(new DueFire(s.Id, s.Name, s.Email, weekStart, "monday", "", week));
                }
            }
        }
        return fires;
    }

    internal async Task SendFireAsync(DueFire f, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IAppDbContext>();
        var notifier = sp.GetRequiredService<INotifier>();

        var (template, body, urgency) = f.Template switch
        {
            "morning" => ("morning", ReminderMessages.MorningNudge(
                f.Name, ReminderSchedule.StudentLocalTime(f.LessonDate), f.MeetLink), NotifyUrgency.Normal),
            "evening" => ("evening", ReminderMessages.EveningNudge(
                f.Name, ReminderSchedule.StudentLocalTime(f.LessonDate), f.MeetLink), NotifyUrgency.High),
            _ => ("monday", ReminderMessages.MondaySummary(f.Name, f.Week ?? []), NotifyUrgency.Normal),
        };

        try
        {
            var result = await notifier.SendAsync(
                f.StudentId, ReminderTitles.For(template), body, urgency, ct);
            db.ReminderLogs.Add(new ReminderLog
            {
                UserId = f.StudentId, To = f.Email, Date = f.LessonDate,
                Template = template, Result = "sent",
                Channel = result.Channel, ProviderRef = result.ProviderRef,
            });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Notification {Template} to {UserId} failed.", template, f.StudentId);
            db.ReminderLogs.Add(new ReminderLog
            {
                UserId = f.StudentId, To = f.Email, Date = f.LessonDate,
                Template = template, Result = "failed",
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static DateTime? Min(DateTime? a, DateTime b) =>
        a is null || b < a ? b : a;
}
