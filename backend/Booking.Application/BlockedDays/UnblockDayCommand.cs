using Booking.Application.Abstractions;

namespace Booking.Application.BlockedDays;

public sealed record UnblockDayCommand(DateOnly Date) : ICommand<bool>;
