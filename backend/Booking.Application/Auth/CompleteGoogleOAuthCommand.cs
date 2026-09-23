using Booking.Application.Abstractions;

namespace Booking.Application.Auth;

public sealed record CompleteGoogleOAuthCommand(string Code, string State) : ICommand<bool>;
