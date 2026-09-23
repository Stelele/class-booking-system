namespace Domain.Slots;

public sealed class Slot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required DateOnly Date { get; set; }
    public string? MeetLink { get; set; }
    public string? GoogleEventId { get; set; }
    public List<Booking> Bookings { get; set; } = [];
}
