namespace Domain.Slots;

public sealed class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid SlotId { get; set; }
    public required Guid StudentId { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Active;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateOnly? OriginalDate { get; set; }
    public Slot Slot { get; set; } = null!;
}
