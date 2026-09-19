using Booking.Application.Abstractions;

namespace Booking.Application.Auth;

public sealed record RequestCodeCommand(string Email) : ICommand<bool>;
