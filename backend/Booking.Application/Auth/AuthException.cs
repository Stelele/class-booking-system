namespace Booking.Application.Auth;

public sealed class AuthException(string message) : Exception(message);
