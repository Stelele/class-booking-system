using Application.Abstractions;

namespace Application.BlockedDays;

public sealed record UnblockDayCommand(DateOnly Date) : ICommand<bool>;
