using Application.Abstractions;

namespace Application.BlockedDays;

public sealed record BlockDayCommand(DateOnly Date, string? Reason) : ICommand<bool>;
