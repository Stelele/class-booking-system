namespace Booking.Application.DTOs;

public sealed record BookingDto(
    Guid Id,
    DateOnly Date,
    DateTime StartUtc,
    string StudentName,
    bool CanCancel,
    bool CanReschedule,
    DateOnly? OriginalDate,
    string? MeetLink);
