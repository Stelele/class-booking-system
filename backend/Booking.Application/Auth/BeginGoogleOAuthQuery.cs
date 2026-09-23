using Booking.Application.Abstractions;

namespace Booking.Application.Auth;

public sealed record BeginGoogleOAuthQuery : IQuery<string>;
