using Booking.Application.Abstractions;
using Booking.Application.Slots;
using Booking.Domain.Slots;
using TimeZoneConverter;

namespace Booking.Endpoints;

public static class IcsEndpoint
{
    public static IEndpointRouteBuilder MapIcs(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/slots/{date}/ics", async (DateOnly date, ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var days = await sender.Send(new GetMonthQuery(date.Year, date.Month), ct);
            var day = days.FirstOrDefault(d => d.Date == date);
            if (day?.MeetLink is null) return Results.NotFound();

            // derive from the single source of truth — no hardcoded times
            var tz = TZConvert.GetTimeZoneInfo(LessonTime.ZoneId);
            var startLocal = TimeZoneInfo.ConvertTimeFromUtc(LessonTime.StartUtc(date), tz);
            var endLocal = TimeZoneInfo.ConvertTimeFromUtc(LessonTime.EndUtc(date), tz);
            var start = startLocal.ToString("yyyyMMdd'T'HHmmss");
            var end = endLocal.ToString("yyyyMMdd'T'HHmmss");

            var ics = string.Join("\r\n",
                "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//ClassBooking//EN",
                "BEGIN:VTIMEZONE", $"TZID:{LessonTime.ZoneId}", "BEGIN:STANDARD",
                "DTSTART:19700101T000000", "TZOFFSETFROM:+0200", "TZOFFSETTO:+0200",
                "TZNAME:CAT", "END:STANDARD", "END:VTIMEZONE",
                "BEGIN:VEVENT",
                $"UID:lesson-{date:yyyyMMdd}@booking",
                $"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}",
                $"DTSTART;TZID={LessonTime.ZoneId}:{start}",
                $"DTEND;TZID={LessonTime.ZoneId}:{end}",
                "SUMMARY:Programming lesson",
                $"LOCATION:{day.MeetLink}",
                $"DESCRIPTION:Lesson with the crew. Join: {day.MeetLink}",
                "END:VEVENT", "END:VCALENDAR");

            http.Response.Headers.ContentDisposition = $"attachment; filename=lesson-{date:yyyyMMdd}.ics";
            return Results.Text(ics, "text/calendar", System.Text.Encoding.UTF8);
        }).RequireAuthorization();
        return app;
    }
}
