using Booking.Application.Abstractions;
using Booking.Application.DTOs;
using Booking.Application.Slots;

namespace Booking.Endpoints;

public static class IcsEndpoint
{
    public static IEndpointRouteBuilder MapIcs(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/slots/{date}/ics", async (DateOnly date, ISender sender) =>
        {
            var days = await sender.Send(new GetMonthQuery(date.Year, date.Month));
            var day = days.FirstOrDefault(d => d.Date == date);
            if (day?.MeetLink is null) return Results.NotFound();

            var start = date.ToString("yyyyMMdd") + "T203000";
            var end = date.ToString("yyyyMMdd") + "T223000";
            var ics = string.Join("\r\n",
                "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//ClassBooking//EN",
                "BEGIN:VTIMEZONE", "TZID:Africa/Harare", "BEGIN:STANDARD",
                "DTSTART:19700101T000000", "TZOFFSETFROM:+0200", "TZOFFSETTO:+0200",
                "TZNAME:CAT", "END:STANDARD", "END:VTIMEZONE",
                "BEGIN:VEVENT",
                $"UID:lesson-{date:yyyyMMdd}@booking",
                $"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}",
                $"DTSTART;TZID=Africa/Harare:{start}",
                $"DTEND;TZID=Africa/Harare:{end}",
                "SUMMARY:Programming lesson",
                $"LOCATION:{day.MeetLink}",
                $"DESCRIPTION:Lesson with the crew. Join: {day.MeetLink}",
                "END:VEVENT", "END:VCALENDAR");

            return Results.Text(ics, "text/calendar", System.Text.Encoding.UTF8);
        }).RequireAuthorization();
        return app;
    }
}
