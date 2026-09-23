namespace Booking.Domain.Reminders;

public sealed class ReminderLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string To { get; set; }
    public required DateOnly Date { get; set; }
    public required string Template { get; set; }
    public required string Result { get; set; }
    public string? TwilioSid { get; set; }
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
