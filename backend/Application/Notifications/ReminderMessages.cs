using System.Globalization;

namespace Application.Notifications;

/// <summary>
/// The lesson reminder wordings (plus cancel/reschedule variants). Pure
/// functions — the same text is delivered as a push notification body and as
/// the email body, so the wording must read well in both.
/// </summary>
public static class ReminderMessages
{
    private static string Day(DateOnly date) =>
        date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    public static string Confirmation(string name, DateOnly date, string localTime, string link) =>
        $"Hi {name}, you're booked for {Day(date)} at {localTime}. Join here: {link}";

    public static string Cancelled(string name, DateOnly date, string localTime) =>
        $"Hi {name}, your lesson on {Day(date)} at {localTime} is cancelled. Rebook any time on the site.";

    public static string Rescheduled(string name, DateOnly newDate, string newLocalTime, DateOnly originalDate, string link) =>
        $"Hi {name}, your lesson moved from {Day(originalDate)} to {Day(newDate)} at {newLocalTime}. Join here: {link}";

    public static string NewBooking(string studentName, DateOnly date, string localTime, string link) =>
        $"New lesson booked: {studentName} on {Day(date)} at {localTime}. Join here: {link}";

    public static string MondaySummary(string name, List<(DateOnly date, string localTime)> lessons)
    {
        var lines = lessons.Select(l => $"- {Day(l.date)} at {l.localTime}");
        return $"Hi {name}, your lessons this week:\n{string.Join("\n", lines)}";
    }

    public static string MorningNudge(string name, string localTime, string link) =>
        $"Hi {name}, lesson tonight at {localTime}. Join here: {link}";

    public static string EveningNudge(string name, string localTime, string link) =>
        $"Hi {name}, your lesson starts in 30 min ({localTime}). Join here: {link}";
}
