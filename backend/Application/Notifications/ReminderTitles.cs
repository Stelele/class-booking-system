namespace Application.Notifications;

/// <summary>
/// Notification titles, keyed by the same template names ReminderLog uses.
/// Push needs a title; email uses it as the subject. Bodies stay in
/// <see cref="ReminderMessages"/>.
/// </summary>
public static class ReminderTitles
{
    public static string For(string template) => template switch
    {
        "confirmation" => "Lesson booked",
        "new_booking" => "New lesson booked",
        "cancelled" => "Lesson cancelled",
        "rescheduled" => "Lesson moved",
        "monday" => "Your week ahead",
        "morning" => "Tonight's lesson",
        "evening" => "Starting soon",
        _ => "Lessons",
    };
}
