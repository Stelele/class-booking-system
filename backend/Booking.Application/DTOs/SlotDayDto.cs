namespace Booking.Application.DTOs;

public enum DayState { Bookable, Booked, Combined, Sunday, Blocked, Past, Cutoff }

public sealed record SlotDayDto(
    DateOnly Date,
    DateTime StartUtc,
    DateTime EndUtc,
    DayState State,
    bool CanBook,
    string? Reason,
    List<string> StudentNames,
    string? MeetLink);
