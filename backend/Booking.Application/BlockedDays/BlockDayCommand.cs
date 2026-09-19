using Booking.Application.Abstractions;

namespace Booking.Application.BlockedDays;

public sealed record BlockDayCommand(DateOnly Date, string? Reason) : ICommand<bool>;
