namespace Domain.Slots;

public sealed class BlockedDay
{
    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
}
